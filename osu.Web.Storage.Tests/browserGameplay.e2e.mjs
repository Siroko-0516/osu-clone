import assert from 'node:assert/strict';
import { chromium } from 'playwright';
import { createServer } from 'node:http';
import { readFile, mkdir } from 'node:fs/promises';
import { resolve, extname } from 'node:path';
import { spawnSync } from 'node:child_process';

const root = resolve(process.env.OSU_WEB_ROOT ?? '../artifacts/web/wwwroot');
const artifacts = resolve('browser-test-results');
await mkdir(artifacts, {recursive:true});
const fixtures = resolve(artifacts, 'maps');
const generated = spawnSync('python3', ['createSmokeMaps.py', fixtures], {stdio:'inherit'});
assert.equal(generated.status, 0);
const types = {'.html':'text/html','.js':'text/javascript','.mjs':'text/javascript','.json':'application/json','.wasm':'application/wasm','.css':'text/css','.dat':'application/octet-stream'};
const server = createServer(async (req,res) => {
    try {
        const pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
        const file = resolve(root, '.' + (pathname === '/' ? '/index.html' : pathname));
        if (!file.startsWith(root + '/')) {res.writeHead(403).end();return;}
        const bytes = await readFile(file);
        res.writeHead(200, {'Content-Type':types[extname(file)] ?? 'application/octet-stream'}).end(bytes);
    } catch { res.writeHead(404).end(); }
});
await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
const browser = await chromium.launch({headless:true,args:['--no-sandbox','--enable-unsafe-swiftshader','--enable-precise-memory-info','--autoplay-policy=no-user-gesture-required']});
const page = await browser.newPage({viewport:{width:1280,height:900}});
page.setDefaultTimeout(60000);
const errors=[];
page.on('pageerror',error=>errors.push(error.message));
page.on('console',message=>{if(message.type()==='error') console.error('BROWSER:',message.text())});
try {
    await page.goto(`http://127.0.0.1:${server.address().port}`, {waitUntil:'networkidle',timeout:120000});
    await page.getByText('Player ready', {exact:true}).waitFor({timeout:120000});
    await page.screenshot({path:resolve(artifacts,'library.png'),fullPage:true});
    // Menu navigation must not be swallowed by the game input bridge.
    await page.getByRole('button',{name:'Settings',exact:true}).focus();
    await page.keyboard.press('Tab');
    assert.notEqual(await page.evaluate(()=>document.activeElement.textContent),'Settings');
    for (let mode=0;mode<4;mode++) {
        await page.locator('input[type=file]').setInputFiles(resolve(fixtures,`mode-${mode}.osz`));
        await page.getByRole('heading',{name:`Browser smoke ${mode}`,exact:true}).waitFor();
        const card=page.locator('.song-card').filter({has:page.getByRole('heading',{name:`Browser smoke ${mode}`,exact:true})});
        await card.locator('.difficulty-list button').first().click();
        await page.locator('.play-section').waitFor({state:'visible'});
        await page.locator('.gameplay-hud').waitFor();
        if (mode===0) {
            await page.keyboard.press('Escape');
            await page.getByRole('heading',{name:'Paused',exact:true}).waitFor();
            const pausedScore=await page.locator('.gameplay-hud').innerText();
            await page.waitForTimeout(800);
            assert.equal(await page.locator('.gameplay-hud').innerText(),pausedScore);
            await page.getByRole('button',{name:'Resume play',exact:true}).click();
        }
        await page.screenshot({path:resolve(artifacts,`mode-${mode}.png`)});
        // Deliberately miss notes: completion must still reach results for all modes.
        await page.getByLabel('Play results',{exact:true}).waitFor({timeout:45000});
        await page.getByText('Saved to this browser.',{exact:true}).waitFor();
        await page.getByRole('button',{name:'Back to songs',exact:true}).click();
        await page.locator('.library-intro').waitFor();
        console.log(`PASS mode ${mode}: import, original ruleset, completion, score save, return`);
    }
    await page.reload({waitUntil:'networkidle'});
    await page.getByText('Player ready',{exact:true}).waitFor();
    assert.equal(await page.locator('.score-row').count(),4);
    assert.equal(await page.locator('.song-card').count(),4);
    await page.setViewportSize({width:390,height:844});
    await page.screenshot({path:resolve(artifacts,'mobile-library.png'),fullPage:true});
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth <= innerWidth), 'mobile layout must not overflow');
    assert.deepEqual(errors,[]);
    console.log('PASS persisted songs/scores, keyboard navigation and mobile layout');
} catch (error) {
    console.error('PAGE STATE:',await page.locator('body').innerText());
    console.error('PAGE ERRORS:',errors);
    await page.screenshot({path:resolve(artifacts,'failure.png'),fullPage:true});
    throw error;
} finally {
    await browser.close();
    await new Promise(resolve=>server.close(resolve));
}
