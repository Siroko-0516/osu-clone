import assert from 'node:assert/strict';

// This helper only imports the caller's local archive into an ephemeral browser
// context. It never copies the archive or captures private map screenshots.
// Example (redirect logs outside browser-test-results if they are to be kept):
// OSU_TEST_ARCHIVE=/local/map.osz OSU_CHROMIUM_EXECUTABLE_PATH=/usr/bin/chromium npm run test:e2e
export async function checkPrivateArchive(browser, baseURL, archivePath) {
    const context = await browser.newContext({viewport:{width:1280,height:900}});
    const page = await context.newPage();
    page.setDefaultTimeout(30000);
    const errors=[];
    page.on('pageerror',error => errors.push(error.message));
    let currentDifficulty=-1, currentAttempt=-1;
    try {
        await page.goto(baseURL,{waitUntil:'networkidle',timeout:120000});
        await page.getByText('Player ready',{exact:true}).waitFor({timeout:120000});
        await page.locator('input[type=file]').setInputFiles(archivePath);
        const card=page.locator('.song-card');
        await card.waitFor();
        assert.equal(await card.count(),1,'the private archive must be isolated from generated fixtures');
        const choices=card.locator('.difficulty-actions > button:first-child');
        const count=await choices.count();
        assert.equal(count,Number(process.env.OSU_TEST_DIFFICULTY_COUNT ?? 9),'all private archive difficulties must be imported');
        console.log(`PRIVATE: imported ${count} difficulties; no archive copies or screenshots will be saved`);
        const samples=[];
        samples.push(await record(page,'idle'));
        const replays=Number(process.env.OSU_TEST_REPLAYS ?? 2);
        assert.ok(Number.isInteger(replays) && replays>=2,'private regression must include a second attempt');
        for(let difficulty=0;difficulty<count;difficulty++) {
            currentDifficulty=difficulty;
            for(let attempt=0;attempt<replays;attempt++) {
                currentAttempt=attempt;
                await choices.nth(difficulty).click();
                // Cold ruleset/pool construction can occupy the single WASM thread;
                // give loading the same deadline as the generated browser suite.
                await page.locator(".gameplay-hud").waitFor({timeout:30000});
                const loaded=await until(page,state => state.managed?.ready && !state.managed.loadingDifficulty,'ready playfield');
                const played=await until(page,state => state.managed.time>loaded.managed.time+1000
                    && state.managed.judged>loaded.managed.judged
                    && state.managed.gameFrameCount>loaded.managed.gameFrameCount+5,'clock, frames and judgements advancing');
                assert.ok(played.frame>loaded.frame,'browser frames must advance');
                samples.push(await record(page,`difficulty-${difficulty+1}/attempt-${attempt+1}/play`,played));
                await page.keyboard.press('Escape');
                await page.getByRole('heading',{name:'Paused',exact:true}).waitFor({timeout:10000});
                const paused=await until(page,state => state.managed.paused===true,'paused clock');
                await page.waitForTimeout(350);
                const stillPaused=await readState(page);
                assert.equal(stillPaused.managed.time,paused.managed.time,'paused clock must remain stopped');
                assert.equal(stillPaused.managed.judged,paused.managed.judged,'pause must not judge more notes');
                await page.getByRole('button',{name:'Resume play',exact:true}).click();
                await until(page,state => state.managed.time>paused.managed.time+300 && !state.managed.paused,'resumed clock');
                await page.keyboard.press('Escape');
                await page.getByRole('heading',{name:'Paused',exact:true}).waitFor({timeout:10000});
                await page.getByRole('button',{name:'Back to songs',exact:true}).click();
                await page.locator('.library-intro').waitFor();
                const returned=await until(page,state => state.managed.ready==null && !state.gameplayActive,'disposed gameplay session');
                samples.push(await record(page,`difficulty-${difficulty+1}/attempt-${attempt+1}/return`,returned));
                assert.deepEqual(errors,[],'private playback must not produce page errors');
            }
        }
        // Report measured memory, without inventing an absolute threshold that
        // would depend on browser/runtime versions or claiming a leak is fixed.
        const peak=key => Math.max(0,...samples.map(s => s[key] ?? 0));
        console.log('PRIVATE SAMPLED MEMORY PEAKS:',JSON.stringify({jsHeapBytes:peak('jsHeapBytes'),wasmBytes:peak('wasmBytes'),managedBytes:peak('managedBytes')}));
        console.log(`PASS private archive: ${count} difficulties × ${replays} attempts, ready/play/pause/resume/exit; this is a bounded local regression, not full-song or mobile verification`);
    } catch(error) {
        console.error(`PRIVATE FAILURE: difficulty index ${currentDifficulty+1}, attempt ${currentAttempt+1}:`,error.message);
        // Exclude body text, map metadata and pixels from failure artifacts.
        try {
            console.error('PRIVATE JS STATE:',JSON.stringify(await bounded(page.evaluate(async() => {
                const state=await (await import('/js/browserHost.js?v=16-render-recovery')).playerDiagnostics(false);
                return {frame:state.frame,pumpPending:state.pumpPending,drawFailures:state.drawFailures,hostFailureReported:state.hostFailureReported,memory:state.memory};
            }),3000)));
        } catch(diagnosticError) { console.error('Private diagnostics unavailable:',diagnosticError.message); }
        throw error;
    } finally { await context.close().catch(() => {}); }
}

async function bounded(operation,milliseconds) {
    let timer;
    try {
        return await Promise.race([operation,new Promise((_,reject) => {
            timer=setTimeout(() => reject(new Error('Managed player diagnostics did not respond')),milliseconds);
        })]);
    } finally { clearTimeout(timer); }
}

async function readState(page) {
    const state=await bounded(page.evaluate(async() => (await import('/js/browserHost.js?v=16-render-recovery')).playerDiagnostics()),5000);
    assert.equal(state.hostFailureReported,false,'browser host must stay responsive');
    assert.equal(state.managed?.gameFailed,false,'managed game must stay alive');
    assert.equal(state.managed?.gameplayError ?? '','','gameplay must not report an error');
    return state;
}

async function until(page,predicate,label) {
    const deadline=Date.now()+30000;
    do {
        const state=await readState(page);
        if(predicate(state)) return state;
        await page.waitForTimeout(200);
    } while(Date.now()<deadline);
    throw new Error(`Timed out waiting for ${label}`);
}

async function record(page,label,state) {
    state ??= await readState(page);
    const memory=await bounded(page.evaluate(() => ({
        jsHeapBytes:performance.memory?.usedJSHeapSize,
        wasmBytes:globalThis.getDotnetRuntime?.(0)?.Module?.HEAPU8?.buffer.byteLength,
    })),5000);
    const sample={label,...memory,managedBytes:state.managed.managedBytes,allocatedBytes:state.managed.allocatedBytes,
        frame:state.frame,gameFrameCount:state.managed.gameFrameCount,time:state.managed.time,judged:state.managed.judged,
        renderer:state.memory};
    console.log('PRIVATE METRIC:',JSON.stringify(sample));
    return sample;
}
