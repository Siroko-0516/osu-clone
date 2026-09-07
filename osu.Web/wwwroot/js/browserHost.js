let animationFrame;
let canvas;
let context;
let dotnet;
let frame = 0;
let pointer = { x: 0, y: 0, down: false };
const keys = new Set();

export function startBrowserHost(target, dotnetReference) {
    canvas = target;
    context = canvas.getContext("2d", { alpha: false });
    dotnet = dotnetReference;

    const resize = () => {
        const ratio = window.devicePixelRatio || 1;
        const rect = canvas.getBoundingClientRect();
        canvas.width = Math.max(1, Math.round(rect.width * ratio));
        canvas.height = Math.max(1, Math.round(rect.height * ratio));
        context.setTransform(ratio, 0, 0, ratio, 0, 0);
    };

    const position = event => {
        const rect = canvas.getBoundingClientRect();
        pointer.x = event.clientX - rect.left;
        pointer.y = event.clientY - rect.top;
    };

    canvas.addEventListener("pointermove", position);
    canvas.addEventListener("pointerdown", event => {
        position(event);
        pointer.down = true;
        canvas.focus();
        dotnet.invokeMethodAsync("ReportInput", "pointer down", pointer.x, pointer.y);
    });
    window.addEventListener("pointerup", () => pointer.down = false);
    canvas.addEventListener("keydown", event => {
        if (event.code !== "KeyZ" && event.code !== "KeyX") return;
        event.preventDefault();
        keys.add(event.code);
        dotnet.invokeMethodAsync("ReportInput", event.code, pointer.x, pointer.y);
    });
    canvas.addEventListener("keyup", event => keys.delete(event.code));
    window.addEventListener("resize", resize);

    resize();
    const render = time => {
        const width = canvas.clientWidth;
        const height = canvas.clientHeight;
        const pulse = (Math.sin(time / 500) + 1) / 2;
        const gradient = context.createRadialGradient(width / 2, height / 2, 10, width / 2, height / 2, width * .7);
        gradient.addColorStop(0, `rgb(${38 + pulse * 18}, 22, 47)`);
        gradient.addColorStop(1, "#08060a");
        context.fillStyle = gradient;
        context.fillRect(0, 0, width, height);

        context.strokeStyle = "rgba(255,255,255,.06)";
        context.lineWidth = 1;
        for (let x = 0; x < width; x += 40) { context.beginPath(); context.moveTo(x, 0); context.lineTo(x, height); context.stroke(); }
        for (let y = 0; y < height; y += 40) { context.beginPath(); context.moveTo(0, y); context.lineTo(width, y); context.stroke(); }

        context.beginPath();
        context.arc(pointer.x || width / 2, pointer.y || height / 2, pointer.down ? 25 : 18, 0, Math.PI * 2);
        context.fillStyle = pointer.down ? "#fff" : "#ef5c9e";
        context.fill();
        context.lineWidth = 5;
        context.strokeStyle = "rgba(255,255,255,.8)";
        context.stroke();

        context.fillStyle = "rgba(255,255,255,.82)";
        context.font = "600 14px Inter, system-ui, sans-serif";
        context.fillText(`requestAnimationFrame · ${Math.round(time)} ms`, 18, 28);
        context.fillText(`Z ${keys.has("KeyZ") ? "DOWN" : "UP"}   X ${keys.has("KeyX") ? "DOWN" : "UP"}`, 18, 50);

        frame++;
        if (frame % 30 === 0) dotnet.invokeMethodAsync("ReportFrame", frame);
        animationFrame = requestAnimationFrame(render);
    };
    animationFrame = requestAnimationFrame(render);
}

export function stopBrowserHost() {
    if (animationFrame) cancelAnimationFrame(animationFrame);
    animationFrame = undefined;
    dotnet = undefined;
}
