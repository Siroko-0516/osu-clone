import assert from 'node:assert/strict';
// Run against an actual browser context: mocked GL calls cannot prove mask or path pixels.
export async function checkRendererPixels(page) {
    const result=await page.evaluate(async()=>{
        const {createFrameworkDraw}=await import('/js/browserDraw.mjs');
        const canvas=document.createElement('canvas');canvas.width=128;canvas.height=128;
        const gl=canvas.getContext('webgl2',{preserveDrawingBuffer:true,alpha:true});
        const texture=gl.createTexture();gl.bindTexture(gl.TEXTURE_2D,texture);
        gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
        gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA,1,1,0,gl.RGBA,gl.UNSIGNED_BYTE,new Uint8Array([255,255,255,255]));
        const renderer=createFrameworkDraw(gl,new Map([[1,texture]]),texture);
        const packet=(path=false)=>{
            const q=new Float32Array(76);
            const positions=path?[[16,40],[112,40],[112,72],[16,72]]:[[16,16],[112,16],[112,112],[16,112]];
            positions.forEach(([x,y],i)=>q.set([x,y,1,1,1,path?.5:1,...(path?[i===0||i===3?-1:5,i<2?-1:1]:[0,0]),1],i*9));
            q.set([0,0,128,128,1,0,0,0,1,0,16,16,96,96,24,0,0,0,0,0,path?0:1,path?1:0,4,0,0,1,1,12,10,5,10,1,1,0,2,1,1,0,0,0],36);
            return q;
        };
        const draw=quads=>{
            const frame=new Float32Array(8+quads.length*76);frame.set([0,0,0,0,quads.length*76,1,128,128]);quads.forEach((q,i)=>frame.set(q,8+i*76));
            gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.viewport(0,0,128,128);gl.disable(gl.SCISSOR_TEST);gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT);
            renderer.render(frame,frame.length,128,128);
        };
        const pixel=(x,y)=>{const p=new Uint8Array(4);gl.readPixels(x,127-y,1,1,gl.RGBA,gl.UNSIGNED_BYTE,p);return [...p];};
        draw([packet()]);const center=pixel(64,64),corner=pixel(17,17),outside=pixel(4,64);
        // Identical overlapping capsules must composite once, not accumulate alpha.
        draw([packet(true),packet(true)]);const overlap=pixel(64,56),capOutside=pixel(17,41);
        const memory=renderer.memory();const error=gl.getError();renderer.dispose();gl.deleteTexture(texture);
        return {center,corner,outside,overlap,capOutside,error,memory};
    });
    assert.ok(result.center[3]>250,JSON.stringify(result));
    assert.equal(result.corner[3],0);assert.equal(result.outside[3],0);
    assert.ok(result.overlap[3]>=125&&result.overlap[3]<=130,JSON.stringify(result));
    assert.equal(result.capOutside[3],0);assert.equal(result.error,0);
    assert.equal(result.memory.pathBytes,128*128*4);
    console.log('PASS actual WebGL rounded masks, capsule caps and overlap-safe path compositing');
}
