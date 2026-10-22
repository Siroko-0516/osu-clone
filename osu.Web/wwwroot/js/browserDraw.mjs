// Version-one framework draw packets. One reusable vertex buffer and one path
// distance target serve every drawable; no per-frame GPU resources are created.
const STRIDE = 76;
const ORDER = [0, 1, 2, 2, 3, 0];
const vertex = `#version 300 es
in vec2 a_position; in vec4 a_colour; in vec2 a_uv;
uniform vec2 u_viewport;
out vec2 v_position; out vec4 v_colour; out vec2 v_uv;
void main(){vec2 p=a_position/u_viewport*2.-1.;gl_Position=vec4(p.x,-p.y,0.,1.);v_position=a_position;v_colour=a_colour;v_uv=a_uv;}`;
const fragment = `#version 300 es
precision highp float;
in vec2 v_position; in vec4 v_colour; in vec2 v_uv;
uniform sampler2D u_texture; uniform sampler2D u_distance;
uniform int u_mode; uniform float u_length; uniform vec2 u_viewport;
uniform vec4 u_gradient; uniform vec3 u_mask_x; uniform vec3 u_mask_y;
uniform vec4 u_rect; uniform vec4 u_border; uniform vec4 u_shape;
uniform vec4 u_extra; uniform float u_mask;
out vec4 colour;
float rounded(vec2 p,vec2 halfSize,float radius,float exponent){
 vec2 q=abs(p)-halfSize+radius;
 vec2 outer=max(q,0.);
 return pow(pow(outer.x,exponent)+pow(outer.y,exponent),1./exponent)+min(max(q.x,q.y),0.)-radius;
}
void main(){
 if(u_mode==1){float d=1.-length(vec2(v_uv.x-clamp(v_uv.x,0.,u_length),v_uv.y));colour=vec4(max(d,0.));return;}
 vec4 sampleColour;
 if(u_mode==2){float d=texture(u_distance,gl_FragCoord.xy/u_viewport).r;if(d<=0.)discard;sampleColour=texture(u_texture,u_gradient.xy+vec2(d,.5)*u_gradient.zw);}
 else sampleColour=texture(u_texture,v_uv);
 vec4 c=sampleColour*v_colour;
 if(u_mask>.5){
   vec2 p=vec2(dot(vec3(v_position,1.),u_mask_x),dot(vec3(v_position,1.),u_mask_y));
   vec2 halfSize=u_rect.zw*.5;
   float radius=min(u_shape.x,min(halfSize.x,halfSize.y));
   float d=rounded(p-u_rect.xy-halfSize,halfSize,radius,max(1.,u_extra.x));
   float aa=max(fwidth(d),.0001);
   float coverage=1.-smoothstep(-aa*.5,aa*.5,d);
   if(u_shape.y>0.){
     float edge=smoothstep(-u_shape.y-aa*.5,-u_shape.y+aa*.5,d);
     c=mix(c,u_border*vec4(1.,1.,1.,v_colour.a),edge);
   }
   if(u_shape.z>.5) coverage*=smoothstep(-u_shape.y-aa,-u_shape.y,d);
   c.a*=coverage;
 }
 colour=c;
}`;
function program(gl) {
    const p=gl.createProgram(), shaders=[];
    try {
        for(const [type,source] of [[gl.VERTEX_SHADER,vertex],[gl.FRAGMENT_SHADER,fragment]]) {
            const s=gl.createShader(type);shaders.push(s);gl.shaderSource(s,source);gl.compileShader(s);
            if(!gl.getShaderParameter(s,gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s));
            gl.attachShader(p,s);
        }
        gl.linkProgram(p);
        if(!gl.getProgramParameter(p,gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
        return p;
    } catch(e){gl.deleteProgram(p);throw e;}
    finally{for(const s of shaders)gl.deleteShader(s);}
}
export function createFrameworkDraw(gl, textures, fallback) {
    const p=program(gl), buffer=gl.createBuffer();
    const attrs=['a_position','a_colour','a_uv'].map(n=>gl.getAttribLocation(p,n));
    const u=Object.fromEntries(['viewport','texture','distance','mode','length','gradient','mask_x','mask_y','rect','border','shape','extra','mask'].map(n=>[n,gl.getUniformLocation(p,'u_'+n)]));
    const blend=[gl.ONE,gl.CONSTANT_ALPHA,gl.CONSTANT_COLOR,gl.DST_ALPHA,gl.DST_COLOR,gl.ONE,gl.ONE_MINUS_CONSTANT_ALPHA,gl.ONE_MINUS_CONSTANT_COLOR,gl.ONE_MINUS_DST_ALPHA,gl.ONE_MINUS_DST_COLOR,gl.ONE_MINUS_SRC_ALPHA,gl.ONE_MINUS_SRC_COLOR,gl.SRC_ALPHA,gl.SRC_ALPHA_SATURATE,gl.SRC_COLOR,gl.ZERO];
    const equations=[gl.FUNC_ADD,gl.FUNC_ADD,gl.MIN,gl.MAX,gl.FUNC_SUBTRACT,gl.FUNC_REVERSE_SUBTRACT];
    let vertices=new Float32Array(0), capacity=0, distanceTexture=null, framebuffer=null, targetWidth=0,targetHeight=0;
    let previousFrame=null;
    let compositeBuffer=null;
    const compositeVertices=new Float32Array(6);
    function ensureTarget(w,h){
        if(targetWidth===w&&targetHeight===h)return;
        if(!distanceTexture){distanceTexture=gl.createTexture();framebuffer=gl.createFramebuffer();}
        gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,distanceTexture);
        gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MIN_FILTER,gl.NEAREST);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_MAG_FILTER,gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
        gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA8,w,h,0,gl.RGBA,gl.UNSIGNED_BYTE,null);
        gl.bindFramebuffer(gl.FRAMEBUFFER,framebuffer);gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,distanceTexture,0);
        if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE)throw new Error('Browser path framebuffer is incomplete');
        targetWidth=w;targetHeight=h;
    }
    function state(data,o,w,h){
        gl.enable(gl.SCISSOR_TEST);
        const x=Math.max(0,data[o+36]),y=Math.max(0,data[o+37]);
        gl.scissor(x,Math.max(0,h-data[o+37]-data[o+39]),Math.max(0,Math.min(w-x,data[o+38])),Math.max(0,Math.min(h-y,data[o+39])));
        gl.uniform3f(u.mask_x,data[o+40],data[o+41],data[o+42]);gl.uniform3f(u.mask_y,data[o+43],data[o+44],data[o+45]);
        gl.uniform4f(u.rect,...data.subarray(o+46,o+50));gl.uniform4f(u.border,...data.subarray(o+52,o+56));
        gl.uniform4f(u.shape,data[o+50],data[o+51],data[o+69],0);gl.uniform4f(u.extra,data[o+70],data[o+71],data[o+72],data[o+73]);
        gl.uniform1f(u.mask,data[o+56]);
        gl.blendFuncSeparate(blend[data[o+63]]??gl.SRC_ALPHA,blend[data[o+64]]??gl.ONE_MINUS_SRC_ALPHA,blend[data[o+65]]??gl.ONE,blend[data[o+66]]??gl.ONE_MINUS_SRC_ALPHA);
        gl.blendEquationSeparate(equations[data[o+67]]??gl.FUNC_ADD,equations[data[o+68]]??gl.FUNC_ADD);
        gl.activeTexture(gl.TEXTURE0);gl.bindTexture(gl.TEXTURE_2D,textures.get(data[o+8])??fallback);
    }
    return {
        render(data,length,w,h,dirty=true){
            const count=Math.floor((length-8)/STRIDE),required=count*48;
            gl.useProgram(p);gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
            if(dirty||previousFrame!==data){
                const next=Math.max(4096,2**Math.ceil(Math.log2(Math.max(1,required))));
                if(vertices.length<required||vertices.length>next*4)vertices=new Float32Array(next);
                let target=0;
                for(let i=0;i<count;i++)for(const v of ORDER)for(let c=0;c<8;c++)vertices[target++]=data[8+i*STRIDE+v*9+c];
                if(capacity!==vertices.byteLength){capacity=vertices.byteLength;gl.bufferData(gl.ARRAY_BUFFER,capacity,gl.DYNAMIC_DRAW);}
                if(required)gl.bufferSubData(gl.ARRAY_BUFFER,0,vertices,0,required);
                previousFrame=data;
            }
            for(let i=0;i<3;i++){gl.enableVertexAttribArray(attrs[i]);gl.vertexAttribPointer(attrs[i],[2,4,2][i],gl.FLOAT,false,32,[0,8,24][i]);}
            gl.uniform2f(u.viewport,w,h);gl.uniform1i(u.texture,0);gl.uniform1i(u.distance,1);
            gl.enable(gl.BLEND);gl.disable(gl.DEPTH_TEST);
            for(let i=0;i<count;i++){
                const o=8+i*STRIDE,id=data[o+57];
                state(data,o,w,h);
                if(id===0){
                    // Preserve painter order while batching adjacent quads with identical GPU state.
                    let end=i+1;
                    while(end<count){
                        const next=8+end*STRIDE;
                        if(data[next+57]!==0||data[next+8]!==data[o+8])break;
                        let equal=true;
                        for(let c=36;c<STRIDE;c++)if(data[next+c]!==data[o+c]){equal=false;break;}
                        if(!equal)break;
                        end++;
                    }
                    gl.uniform1i(u.mode,0);gl.drawArrays(gl.TRIANGLES,i*6,(end-i)*6);i=end-1;continue;
                }
                let end=i+1;while(end<count&&data[8+end*STRIDE+57]===id)end++;
                ensureTarget(w,h);gl.bindFramebuffer(gl.FRAMEBUFFER,framebuffer);
                // No read/write feedback: unbind the target from every sampler during prepass.
                gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,null);
                // Restrict clear, distance rasterisation and composite to this path's bounds.
                let left=w,top=h,right=0,bottom=0;
                for(let j=i;j<end;j++)for(let v=0;v<4;v++){
                    const offset=8+j*STRIDE+v*9;
                    left=Math.min(left,data[offset]);right=Math.max(right,data[offset]);
                    top=Math.min(top,data[offset+1]);bottom=Math.max(bottom,data[offset+1]);
                }
                left=Math.max(0,Math.floor(left));top=Math.max(0,Math.floor(top));
                right=Math.min(w,Math.ceil(right));bottom=Math.min(h,Math.ceil(bottom));
                gl.enable(gl.SCISSOR_TEST);gl.scissor(left,h-bottom,Math.max(0,right-left),Math.max(0,bottom-top));
                gl.clearColor(0,0,0,0);gl.clear(gl.COLOR_BUFFER_BIT);
                gl.blendFunc(gl.ONE,gl.ONE);gl.blendEquation(gl.MAX);gl.uniform1i(u.mode,1);
                for(let j=i;j<end;j++){gl.uniform1f(u.length,data[8+j*STRIDE+58]);gl.drawArrays(gl.TRIANGLES,j*6,6);}
                gl.bindFramebuffer(gl.FRAMEBUFFER,null);state(data,o,w,h);
                left=Math.max(left,data[o+36]);top=Math.max(top,data[o+37]);
                right=Math.min(right,data[o+36]+data[o+38]);bottom=Math.min(bottom,data[o+37]+data[o+39]);
                gl.scissor(left,h-bottom,Math.max(0,right-left),Math.max(0,bottom-top));
                gl.activeTexture(gl.TEXTURE1);gl.bindTexture(gl.TEXTURE_2D,distanceTexture);
                gl.uniform4f(u.gradient,...data.subarray(o+59,o+63));gl.uniform1i(u.mode,2);
                // Composite each covered pixel exactly once, even at self intersections.
                // A stencil-free union is achieved using one bounding rectangle from a scissored fullscreen triangle.
                gl.disableVertexAttribArray(attrs[1]);gl.vertexAttrib4f(attrs[1],...data.subarray(o+2,o+6));
                gl.disableVertexAttribArray(attrs[2]);gl.vertexAttrib2f(attrs[2],0,0);
                // Separate fixed-size composite buffer avoids replacing the reusable scene buffer.
                composite(w,h,attrs[0]);
                gl.bindBuffer(gl.ARRAY_BUFFER,buffer);
                for(let a=0;a<3;a++){gl.enableVertexAttribArray(attrs[a]);gl.vertexAttribPointer(attrs[a],[2,4,2][a],gl.FLOAT,false,32,[0,8,24][a]);}
                i=end-1;
            }
            gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.disable(gl.SCISSOR_TEST);gl.blendEquation(gl.FUNC_ADD);
        },
        dispose(){gl.deleteBuffer(buffer);gl.deleteBuffer(compositeBuffer);gl.deleteProgram(p);if(framebuffer)gl.deleteFramebuffer(framebuffer);if(distanceTexture)gl.deleteTexture(distanceTexture);vertices=new Float32Array(0);},
        memory(){return {vertexBytes:vertices.byteLength,gpuVertexBytes:capacity,pathBytes:targetWidth*targetHeight*4};}
    };
    function composite(w,h,attribute){
        if(!compositeBuffer)compositeBuffer=gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER,compositeBuffer);
        compositeVertices.set([0,0,2*w,0,0,2*h]);
        gl.bufferData(gl.ARRAY_BUFFER,compositeVertices,gl.DYNAMIC_DRAW);gl.enableVertexAttribArray(attribute);gl.vertexAttribPointer(attribute,2,gl.FLOAT,false,8,0);gl.drawArrays(gl.TRIANGLES,0,3);
    }
}
