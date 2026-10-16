// React Bits Pattern Waves, Copyright (c) 2026 David Haz.
// MIT + Commons Clause; see public/design/licenses/react-bits.txt.
// React lifecycle port of the supplied design handoff, covered by LICENSE-EXCEPTION.md.
// Usage: <pattern-waves color="#667cdb" background-color="#000" shine="0.4" direction="62" fade-size="0.6"></pattern-waves>
import { Renderer, Program, Mesh, Triangle, RenderTarget, Texture } from 'ogl';

const PRESET = { pattern: 'dot', wave: 'silk', spacing: 9, markSize: 0.95, depth: 0.95, light: 0, shine: 0.8, contrast: 1.2, speed: 0.35, scale: 1, direction: 20 };
const PATTERNS = { dot: 0, square: 1, plus: 2, line: 3 };
const WAVES = { silk: 0, swell: 1, ripple: 2 };
const FADES = { none: 0, edges: 1, center: 2, bottom: 3, top: 4 };
const WAVE_UNIT = 520, RIPPLE_CELL = 8, RIPPLE_RATE = 60, PIXEL_BUDGET = 4.5e6, INTRO_SECONDS = 2;
const clamp = (v, a, b) => Math.min(Math.max(v, a), b);
const luminance = c => 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
const parseColor = (value, fallback) => {
  try {
    const ctx = document.createElement('canvas').getContext('2d');
    ctx.fillStyle = '#000000'; ctx.fillStyle = value;
    const r = ctx.fillStyle;
    if (r.startsWith('#')) { const n = parseInt(r.slice(1), 16); return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255, 1]; }
    const p = r.match(/[\d.]+/g);
    return [p[0] / 255, p[1] / 255, p[2] / 255, p[3] ? Number(p[3]) : 1];
  } catch { return fallback; }
};

const passVertex = `#version 300 es
in vec2 position;
void main(){gl_Position=vec4(position,0.0,1.0);}`;

const fieldFragment = `#version 300 es
precision highp float;
precision highp int;
uniform vec2 uSize;uniform float uDpr;uniform vec2 uOrigin;uniform vec2 uPitch;uniform int uWave;uniform float uTime;uniform float uUnit;uniform vec2 uHeading;uniform float uAmp;uniform float uDepth;uniform vec3 uLight;uniform float uShine;uniform float uContrast;uniform float uInk;uniform float uOpacity;uniform int uFade;uniform float uFadeSize;uniform float uAppear;uniform sampler2D tRipple;uniform float uRipple;
out vec4 fragColor;
const float FOLDS=5.5;
uvec3 scramble(uvec3 v){v=v*1664525u+1013904223u;v.x+=v.y*v.z;v.y+=v.z*v.x;v.z+=v.x*v.y;v^=v>>16u;v.x+=v.y*v.z;v.y+=v.z*v.x;v.z+=v.x*v.y;return v;}
vec3 lattice(vec3 c){uvec3 h=scramble(uvec3(ivec3(c)+4096));return vec3(h&65535u)/32767.5-1.0;}
float gradientNoise(vec3 p){vec3 i=floor(p);vec3 f=p-i;vec3 u=f*f*f*(f*(f*6.0-15.0)+10.0);
float n000=dot(lattice(i),f);float n100=dot(lattice(i+vec3(1,0,0)),f-vec3(1,0,0));float n010=dot(lattice(i+vec3(0,1,0)),f-vec3(0,1,0));float n110=dot(lattice(i+vec3(1,1,0)),f-vec3(1,1,0));
float n001=dot(lattice(i+vec3(0,0,1)),f-vec3(0,0,1));float n101=dot(lattice(i+vec3(1,0,1)),f-vec3(1,0,1));float n011=dot(lattice(i+vec3(0,1,1)),f-vec3(0,1,1));float n111=dot(lattice(i+vec3(1,1,1)),f-vec3(1,1,1));
return mix(mix(mix(n000,n100,u.x),mix(n010,n110,u.x),u.y),mix(mix(n001,n101,u.x),mix(n011,n111,u.x),u.y),u.z);}
float surface(vec2 p,float t){
if(uWave==0){vec2 side=vec2(-uHeading.y,uHeading.x);float u=dot(p,uHeading);float v=dot(p,side);
float bend=gradientNoise(vec3(v*0.85,u*0.3,t*0.05))*1.7+0.4*sin(v*1.6+t*0.2);float phase=u*FOLDS+bend-t*0.45;
float swell=0.6+0.4*gradientNoise(vec3(u*0.55+3.0,v*0.45,t*0.04));float fold=sin(phase)+0.32*sin(2.0*phase+1.3);
float ripple=0.16*sin(u*FOLDS*2.5+bend*1.9-t*0.9+2.1);return (fold+ripple)*swell;}
if(uWave==1){float bend=gradientNoise(vec3(p*0.6,t*0.05))*0.6;float phase=dot(p,uHeading)*15.0+bend*2.2-t*1.4;float swell=sin(phase)+0.3*sin(2.0*phase-0.8);float roll=0.75+0.25*gradientNoise(vec3(p*0.9+11.0,t*0.05));return 0.8*swell*roll;}
float r=length(p+uHeading*1.2);float bend=gradientNoise(vec3(p*1.2,t*0.05))*0.1;return sin((r+bend)*9.0-t*1.8)*(0.45+0.55*exp(-(r-0.6)*0.8));}
float heightAt(vec2 css){float h=surface((css-0.5*uSize)/uUnit,uTime)*uAmp;if(uRipple>0.0)h+=texture(tRipple,css/uSize).r*uRipple;return h;}
void main(){
vec2 cell=floor(gl_FragCoord.xy);vec2 center=uOrigin+(cell+0.5)*uPitch;vec2 css=center/uDpr;vec2 uv=css/uSize;
float e=max(uPitch.y/uDpr,4.0);float h=heightAt(css);
float hx=(heightAt(css+vec2(e,0.0))-heightAt(css-vec2(e,0.0)))/(2.0*e);float hy=(heightAt(css+vec2(0.0,e))-heightAt(css-vec2(0.0,e)))/(2.0*e);
vec2 grad=vec2(hx,hy)*uUnit*uDepth*0.4;vec3 n=normalize(vec3(-grad,1.0));
float diffuse=clamp(dot(n,uLight),0.0,1.0);vec3 halfway=normalize(uLight+vec3(0,0,1));
float spec=pow(clamp(dot(n,halfway),0.0,1.0),160.0)*uShine*1.15;float hollow=0.7+0.3*clamp(h*0.5+0.5,0.0,1.0);
float tone=clamp(diffuse*hollow*0.78+spec,0.0,1.0);tone=clamp((tone-0.42)*uContrast+0.42,0.0,1.0);
float level=uInk>0.5?pow(clamp(1.0-tone/0.46,0.0,1.0),2.4)*0.72:pow(tone,2.2);
float emphasis=uInk>0.5?smoothstep(0.55,0.95,level):clamp(spec*1.6,0.0,1.0);
float fade=1.0;vec2 c=uv*2.0-1.0;
if(uFade==1){fade=1.0-smoothstep(1.0-uFadeSize,1.18,length(c));}
else if(uFade==2){fade=mix(0.05,1.0,smoothstep(0.08,0.08+uFadeSize,length(c*vec2(1.0,1.35))));}
else if(uFade==3){fade=smoothstep(0.0,uFadeSize,uv.y);}
else if(uFade==4){fade=smoothstep(0.0,uFadeSize,1.0-uv.y);}
float reach=length(css-0.5*uSize)/max(0.5*length(uSize),1.0);float appear=smoothstep(reach-0.05,reach+0.3,uAppear*1.35);
float alpha=uOpacity*fade*appear*(0.22+0.78*level);float lift=clamp(h*uDepth*0.42,-0.48,0.48);
fragColor=vec4(level,alpha,emphasis,lift+0.5);}`;

const rippleFragment = `#version 300 es
precision highp float;
uniform sampler2D tState;uniform vec2 uTexel;uniform vec2 uSize;uniform vec2 uFrom;uniform vec2 uTo;uniform float uRadius;uniform float uImpulse;uniform float uDamping;
out vec4 fragColor;
void main(){vec2 uv=gl_FragCoord.xy*uTexel;vec2 state=texture(tState,uv).rg;
float l=texture(tState,uv-vec2(uTexel.x,0.0)).r;float r=texture(tState,uv+vec2(uTexel.x,0.0)).r;float b=texture(tState,uv-vec2(0.0,uTexel.y)).r;float a=texture(tState,uv+vec2(0.0,uTexel.y)).r;
float next=((l+r+b+a)*0.5-state.g)*uDamping;vec2 p=uv*uSize;vec2 seg=uTo-uFrom;
float along=clamp(dot(p-uFrom,seg)/max(dot(seg,seg),1e-4),0.0,1.0);float d=length(p-uFrom-seg*along)/uRadius;
next-=uImpulse*exp(-d*d*2.0);fragColor=vec4(next,state.r,0.0,1.0);}`;

const markFragment = `#version 300 es
precision highp float;
precision highp int;
uniform sampler2D tField;uniform vec2 uOrigin;uniform vec2 uPitch;uniform vec2 uGrid;uniform int uPattern;uniform float uMarkSize;uniform float uStroke;uniform vec3 uColor;uniform vec3 uAccent;uniform vec4 uBackground;
out vec4 fragColor;
float box(vec2 p,vec2 b,float r){vec2 q=abs(p)-b+r;return length(max(q,0.0))+min(max(q.x,q.y),0.0)-r;}
float coverage(vec2 local,float level){float span=uPitch.y*uMarkSize;float area=max(sqrt(level),0.14);
if(uPattern==0)return clamp(0.5-(length(local)-0.5*span*area),0.0,1.0);
if(uPattern==1){float ex=0.5*span*area;return clamp(0.5-box(local,vec2(ex),ex*0.3),0.0,1.0);}
float arm=0.5*span*mix(0.3,1.0,level);float w=0.5*uStroke;return clamp(0.5-min(box(local,vec2(arm,w),w),box(local,vec2(w,arm),w)),0.0,1.0);}
void main(){vec2 rel=gl_FragCoord.xy-uOrigin;vec4 background=vec4(uBackground.rgb*uBackground.a,uBackground.a);vec4 ink=vec4(0.0);
float cx=floor(rel.x/uPitch.x);float row=floor(rel.y/uPitch.y);
if(cx>=0.0&&cx<uGrid.x){for(int k=-1;k<=1;k++){float cy=row+float(k);if(cy<0.0||cy>=uGrid.y)continue;
vec4 f=texelFetch(tField,ivec2(int(cx),int(cy)),0);if(f.g<0.002)continue;
vec2 center=(vec2(cx,cy)+0.5)*uPitch+vec2(0.0,(f.a-0.5)*uPitch.y);float alpha=coverage(rel-center,f.r)*f.g;
if(alpha>ink.a)ink=vec4(mix(uColor,uAccent,f.b)*alpha,alpha);}}
fragColor=ink+background*(1.0-ink.a);}`;


export function mountPatternWaves(container, notify, frozen = false) {
const s = { ...PRESET, color:[74/255,51/255,168/255,1], background:[8/255,6/255,26/255,1], shine:0.15, contrast:0.9, opacity:0.55, direction:62, fade:'none', fadeSize:0.5, interactive:false, cursorStrength:0, cursorSize:50, intro:!frozen, speed:frozen ? 0 : 0.7, paused:false };
    const renderer = new Renderer({ alpha: true, premultipliedAlpha: true, antialias: false, depth: false });
    const gl = renderer.gl;
    if (!renderer.isWebgl2) { gl.getExtension('WEBGL_lose_context')?.loseContext(); return null; }
    gl.clearColor(0, 0, 0, 0);
    const canvas = gl.canvas;
    canvas.style.cssText = 'display:block;width:100%;height:100%';
    canvas.setAttribute('aria-hidden', 'true');
    container.appendChild(canvas);
    const geometry = new Triangle(gl);
    const blank = new Texture(gl);
    const floatTargets = !!gl.getExtension('EXT_color_buffer_float');
    const dispose = t => { gl.deleteFramebuffer(t.buffer); gl.deleteTexture(t.texture.texture); };
    const fieldTargetFor = (w, h) => new RenderTarget(gl, { width: w, height: h, depth: false, minFilter: gl.NEAREST, magFilter: gl.NEAREST });
    const rippleTargetFor = (w, h) => new RenderTarget(gl, { width: w, height: h, depth: false, type: gl.HALF_FLOAT, format: gl.RGBA, internalFormat: gl.RGBA16F, minFilter: gl.LINEAR, magFilter: gl.LINEAR });
    let fieldTarget = fieldTargetFor(1, 1);
    let ripple = null;
    const fu = { uSize: { value: [1, 1] }, uDpr: { value: 1 }, uOrigin: { value: [0, 0] }, uPitch: { value: [1, 1] }, uWave: { value: 0 }, uTime: { value: 0 }, uUnit: { value: WAVE_UNIT }, uHeading: { value: [1, 0] }, uAmp: { value: 0 }, uDepth: { value: 0.5 }, uLight: { value: [0, 0, 1] }, uShine: { value: 0.8 }, uContrast: { value: 1 }, uInk: { value: 0 }, uOpacity: { value: 1 }, uFade: { value: 0 }, uFadeSize: { value: 0.5 }, uAppear: { value: 0 }, tRipple: { value: blank }, uRipple: { value: 0 } };
    const fieldMesh = new Mesh(gl, { geometry, program: new Program(gl, { vertex: passVertex, fragment: fieldFragment, uniforms: fu, depthTest: false, depthWrite: false }) });
    const ru = { tState: { value: blank }, uTexel: { value: [1, 1] }, uSize: { value: [1, 1] }, uFrom: { value: [0, 0] }, uTo: { value: [0, 0] }, uRadius: { value: 40 }, uImpulse: { value: 0 }, uDamping: { value: 0.975 } };
    const rippleMesh = new Mesh(gl, { geometry, program: new Program(gl, { vertex: passVertex, fragment: rippleFragment, uniforms: ru, depthTest: false, depthWrite: false }) });
    const mu = { tField: { value: fieldTarget.texture }, uOrigin: { value: [0, 0] }, uPitch: { value: [1, 1] }, uGrid: { value: [1, 1] }, uPattern: { value: 0 }, uMarkSize: { value: 0.9 }, uStroke: { value: 2 }, uColor: { value: [1, 1, 1] }, uAccent: { value: [1, 1, 1] }, uBackground: { value: [0, 0, 0, 1] } };
    const markMesh = new Mesh(gl, { geometry, program: new Program(gl, { vertex: passVertex, fragment: markFragment, uniforms: mu, depthTest: false, depthWrite: false }) });
    const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
    let reducedMotion = motion.matches;
    const timing = gl.getExtension('EXT_disjoint_timer_query_webgl2');
    container.dataset.gpuTiming = timing ? 'supported' : 'unavailable';
    let gpuQuery = null, renderFrames = 0;
    const gpuSamples = [];
    let width = 1, height = 1, raf = 0, last = performance.now(), time = 0, introClock = 0, visible = true, alive = true, dirty = true, rippleUntil = 0, rippleLive = false, rippleClock = 0;
    const pointer = { x: 0, y: 0, inside: false, placed: false, lastX: 0, lastY: 0, burst: 0 };
    const buildRipple = () => {
      if (!floatTargets || !s.interactive) return;
      const w = clamp(Math.ceil(width / RIPPLE_CELL), 4, 512), h = clamp(Math.ceil(height / RIPPLE_CELL), 4, 512);
      if (ripple && ripple.width === w && ripple.height === h) return;
      if (ripple) { dispose(ripple.read); dispose(ripple.write); }
      ripple = { width: w, height: h, read: rippleTargetFor(w, h), write: rippleTargetFor(w, h) };
      rippleLive = false;
    };
    const clearRipple = () => {
      if (!ripple) return;
      [ripple.read, ripple.write].forEach(t => { renderer.bindFramebuffer(t); gl.viewport(0, 0, t.width, t.height); gl.clear(gl.COLOR_BUFFER_BIT); });
      renderer.bindFramebuffer();
    };
    const resize = () => {
      if (!alive) return;
      const cw0 = container.clientWidth, ch0 = container.clientHeight;
      if (!cw0 || !ch0) { requestAnimationFrame(resize); return; }
      width = cw0; height = ch0;
      renderer.dpr = Math.min(window.devicePixelRatio || 1, 2, Math.sqrt(PIXEL_BUDGET / (width * height)));
      renderer.setSize(width, height);
      canvas.style.width = '100%'; canvas.style.height = '100%';
      buildRipple(); dirty = true; start();
    };
    const stepRipple = (field, dt) => {
      rippleClock = Math.min(rippleClock + dt * RIPPLE_RATE, 4);
      const strength = clamp(s.cursorStrength, 0, 1);
      const moved = Math.hypot(pointer.x - pointer.lastX, pointer.y - pointer.lastY);
      let impulse = (pointer.inside ? Math.min(moved / 14, 1) * 0.35 * strength : 0) + pointer.burst * strength;
      pointer.burst = 0;
      ru.uTexel.value = [1 / field.width, 1 / field.height]; ru.uSize.value = [width, height];
      ru.uFrom.value = [pointer.lastX, height - pointer.lastY]; ru.uTo.value = [pointer.x, height - pointer.y];
      ru.uRadius.value = clamp(s.cursorSize, 8, 400);
      while (rippleClock >= 1) {
        rippleClock -= 1; ru.tState.value = field.read.texture; ru.uImpulse.value = impulse;
        renderer.render({ scene: rippleMesh, target: field.write, clear: false });
        const n = field.read; field.read = field.write; field.write = n; impulse = 0;
      }
      pointer.lastX = pointer.x; pointer.lastY = pointer.y;
    };
    const frame = now => {
      raf = 0; if (!alive) return;
      if (gpuQuery && gl.getQueryParameter(gpuQuery, gl.QUERY_RESULT_AVAILABLE)) {
        if (!gl.getParameter(timing.GPU_DISJOINT_EXT)) {
          gpuSamples.push(gl.getQueryParameter(gpuQuery, gl.QUERY_RESULT) / 1e6);
          if (gpuSamples.length > 30) gpuSamples.shift();
          container.dataset.gpuMs = (gpuSamples.reduce((a, b) => a + b, 0) / gpuSamples.length).toFixed(3);
          container.dataset.gpuP95Ms = [...gpuSamples].sort((a, b) => a - b)[Math.ceil(gpuSamples.length * .95) - 1].toFixed(3);
          container.dataset.gpuSamples = String(gpuSamples.length);
        }
        gl.deleteQuery(gpuQuery); gpuQuery = null;
      }
      const sample = timing && !gpuQuery && renderFrames % 12 === 0;
      if (sample) { gpuQuery = gl.createQuery(); gl.beginQuery(timing.TIME_ELAPSED_EXT, gpuQuery); }
      const dt = Math.min(0.05, Math.max(1 / 240, (now - last) / 1000)); last = now;
      const moving = !s.paused && !reducedMotion && s.speed !== 0;
      if (moving) time += dt * s.speed;
      introClock = s.intro && !reducedMotion ? Math.min(1, introClock + dt / INTRO_SECONDS) : 1;
      const appear = 1 - Math.pow(1 - clamp(introClock / 0.75, 0, 1), 3);
      const rise = clamp((introClock - 0.1) / 0.9, 0, 1);
      const amp = rise * rise * (3 - 2 * rise);
      const rippling = s.interactive && !reducedMotion && !!ripple && s.cursorStrength > 0;
      if (rippling && pointer.inside && !pointer.placed) { pointer.lastX = pointer.x; pointer.lastY = pointer.y; pointer.placed = true; }
      if (rippling && (pointer.inside || pointer.burst > 0)) rippleUntil = now + 5000;
      const rippleActive = rippling && now < rippleUntil;
      if (rippleActive && ripple) { stepRipple(ripple, dt); rippleLive = true; } else if (rippleLive) { clearRipple(); rippleLive = false; }
      const pi = PATTERNS[s.pattern] ?? 0;
      const cw = gl.canvas.width, ch = gl.canvas.height, dpr = cw / width;
      const pitchY = Math.max(4, Math.round(clamp(s.spacing, 4, 120) * dpr)), pitchX = pitchY;
      const cols = Math.min(4096, Math.ceil(cw / pitchX) + 1), rows = Math.min(4096, Math.ceil(ch / pitchY) + 2);
      const origin = [Math.floor((cw - cols * pitchX) / 2), Math.floor((ch - rows * pitchY) / 2)];
      if (fieldTarget.width !== cols || fieldTarget.height !== rows) { dispose(fieldTarget); fieldTarget = fieldTargetFor(cols, rows); mu.tField.value = fieldTarget.texture; }
      const heading = (s.direction * Math.PI) / 180;
      const lightAngle = ((s.direction + 180 + clamp(s.light, -90, 90)) * Math.PI) / 180;
      const bg = s.background;
      const ink = bg[3] > 0.02 ? luminance(s.color) < luminance(bg) : luminance(s.color) < 0.5;
      fu.uSize.value = [width, height]; fu.uDpr.value = dpr; fu.uOrigin.value = origin; fu.uPitch.value = [pitchX, pitchY];
      fu.uWave.value = WAVES[s.wave] ?? 0; fu.uTime.value = time; fu.uUnit.value = WAVE_UNIT * clamp(s.scale, 0.2, 5);
      fu.uHeading.value = [Math.cos(heading), Math.sin(heading)]; fu.uAmp.value = amp; fu.uDepth.value = clamp(s.depth, 0, 1.5);
      fu.uLight.value = [Math.cos(lightAngle) * 0.78, Math.sin(lightAngle) * 0.78, 0.62];
      fu.uShine.value = clamp(s.shine, 0, 2); fu.uContrast.value = clamp(s.contrast, 0.3, 3); fu.uInk.value = ink ? 1 : 0;
      fu.uOpacity.value = clamp(s.opacity, 0, 1); fu.uFade.value = FADES[s.fade] ?? 0; fu.uFadeSize.value = clamp(s.fadeSize, 0.05, 1);
      fu.uAppear.value = appear; fu.tRipple.value = ripple && rippleLive ? ripple.read.texture : blank; fu.uRipple.value = rippleLive ? 0.32 : 0;
      renderer.render({ scene: fieldMesh, target: fieldTarget });
      mu.uOrigin.value = origin; mu.uPitch.value = [pitchX, pitchY]; mu.uGrid.value = [cols, rows]; mu.uPattern.value = pi;
      mu.uMarkSize.value = clamp(s.markSize, 0.05, 1); mu.uStroke.value = Math.max(1.1 * dpr, pitchY * 0.08);
      const tint = ink ? 0 : 1, blend = ink ? 0.35 : 0.55;
      mu.uColor.value = s.color.slice(0, 3); mu.uAccent.value = s.color.slice(0, 3).map(c => c + (tint - c) * blend); mu.uBackground.value = bg;
      renderer.render({ scene: markMesh });
      if (sample) gl.endQuery(timing.TIME_ELAPSED_EXT);
      renderFrames++;
      container.dataset.renderFrames = String(renderFrames);
      container.dataset.renderPixels = String(canvas.width * canvas.height);
      notify(canvas);
      dirty = false;
      if (visible && !document.hidden && (moving || introClock < 1 || rippleActive || dirty)) raf = requestAnimationFrame(frame);
    };
    const start = () => { if (raf || !visible || !alive) return; last = performance.now(); raf = requestAnimationFrame(frame); };
    const locate = e => { const r = container.getBoundingClientRect(); const x = e.clientX - r.left, y = e.clientY - r.top; return { x, y, inside: x >= 0 && y >= 0 && x <= r.width && y <= r.height }; };
    const onMove = e => { const p = locate(e); pointer.x = p.x; pointer.y = p.y; if (p.inside !== pointer.inside) pointer.placed = false; pointer.inside = p.inside; if (p.inside) start(); };
    const onDown = e => { const p = locate(e); if (!p.inside) return; pointer.x = p.x; pointer.y = p.y; if (!pointer.inside) { pointer.lastX = p.x; pointer.lastY = p.y; } pointer.burst = 1.2; start(); };
    const onLeave = () => { pointer.inside = false; pointer.placed = false; start(); };
    const onOut = e => { if (!e.relatedTarget) onLeave(); };
    const onUp = e => { if (e.pointerType === 'touch') onLeave(); };
    const onVis = () => { if (document.hidden) { cancelAnimationFrame(raf); raf = 0; } else start(); };
    const onMotion = () => { reducedMotion = motion.matches; if (reducedMotion) introClock = 1; start(); };
    motion.addEventListener("change", onMotion);
    window.addEventListener('pointermove', onMove, { passive: true });
    window.addEventListener('pointerdown', onDown, { passive: true });
    window.addEventListener('pointerout', onOut, { passive: true });
    window.addEventListener('pointerup', onUp, { passive: true });
    window.addEventListener('blur', onLeave);
    document.addEventListener('visibilitychange', onVis);
    const ro = new ResizeObserver(resize); ro.observe(container);
    const io = new IntersectionObserver(([en]) => { visible = en.isIntersecting; start(); }); io.observe(container);
    resize();
    requestAnimationFrame(() => requestAnimationFrame(resize));
    return { redraw: start, dispose: () => {
      alive = false; visible = false; cancelAnimationFrame(raf); ro.disconnect(); io.disconnect();
      window.removeEventListener('pointermove', onMove); window.removeEventListener('pointerdown', onDown);
      window.removeEventListener('pointerout', onOut); window.removeEventListener('pointerup', onUp);
      window.removeEventListener('blur', onLeave); document.removeEventListener('visibilitychange', onVis); motion.removeEventListener('change', onMotion);
      if (ripple) { dispose(ripple.read); dispose(ripple.write); }
      if (gpuQuery) gl.deleteQuery(gpuQuery);
      dispose(fieldTarget); gl.getExtension('WEBGL_lose_context')?.loseContext();
      if (canvas.parentNode) canvas.parentNode.removeChild(canvas);
    } };
}
