// 從照片取出主色，設成該區塊的 --mb-accent。
//
// 這是整個視覺方向的核心：介面的重點色不是設計時挑的，是從使用者自己那張
// 照片算出來的，所以每個作品都有自己的顏色。
//
// 用法：在 <img> 加 data-accent-src，並用 data-accent-scope 指定要染色的祖先
// 選擇器（省略則染 :root）。照片必須同源，否則 canvas 會被污染而讀不到像素——
// 我們只取樣 /uploads/ 底下的圖，YouTube 縮圖不取樣。

(() => {
    "use strict";

    const SAMPLE_SIZE = 24;   // 取樣用的縮圖邊長，夠judge主色又夠快
    const HUE_BUCKETS = 12;   // 每桶 30°

    // 照片可能整體偏暗或偏灰，直接拿平均色當重點色會是一坨爛泥。
    // 所以只保留「有顏色資訊」的像素，最後再把亮度與飽和度拉進可讀區間。
    const MIN_LIGHTNESS = 0.12;
    const MAX_LIGHTNESS = 0.92;
    const MIN_SATURATION = 0.15;

    const OUT_LIGHTNESS = 0.62;   // 在 #0A0A0B 底色上這個亮度最耐看
    const OUT_SAT_MIN = 0.45;
    const OUT_SAT_MAX = 0.85;

    const FALLBACK = "#9AA0A6";   // 純灰階照片沒有主色可取時用的中性色

    function rgbToHsl(r, g, b) {
        r /= 255; g /= 255; b /= 255;
        const max = Math.max(r, g, b), min = Math.min(r, g, b);
        const l = (max + min) / 2;
        if (max === min) return [0, 0, l];

        const d = max - min;
        const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        let h;
        if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
        else if (max === g) h = ((b - r) / d + 2) / 6;
        else h = ((r - g) / d + 4) / 6;
        return [h, s, l];
    }

    function hslToHex(h, s, l) {
        const f = n => {
            const k = (n + h * 12) % 12;
            const a = s * Math.min(l, 1 - l);
            const v = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1));
            return Math.round(v * 255).toString(16).padStart(2, "0");
        };
        return `#${f(0)}${f(8)}${f(4)}`;
    }

    /// 取主色：依色相分桶，用飽和度加權投票選出最強的一桶，再正規化成可讀的顏色。
    /// 用分桶而不是取平均，是因為平均會把互補色混成灰。
    function dominantColor(image) {
        const canvas = document.createElement("canvas");
        canvas.width = canvas.height = SAMPLE_SIZE;
        const ctx = canvas.getContext("2d", { willReadFrequently: true });
        ctx.drawImage(image, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE);

        let data;
        try {
            data = ctx.getImageData(0, 0, SAMPLE_SIZE, SAMPLE_SIZE).data;
        } catch {
            return FALLBACK;   // 跨來源圖片會污染 canvas，讀不到就放棄取樣
        }

        const weight = new Array(HUE_BUCKETS).fill(0);
        const hueSum = new Array(HUE_BUCKETS).fill(0);
        const satSum = new Array(HUE_BUCKETS).fill(0);

        for (let i = 0; i < data.length; i += 4) {
            if (data[i + 3] < 128) continue;   // 略過透明像素
            const [h, s, l] = rgbToHsl(data[i], data[i + 1], data[i + 2]);
            if (l < MIN_LIGHTNESS || l > MAX_LIGHTNESS || s < MIN_SATURATION) continue;

            const bucket = Math.min(HUE_BUCKETS - 1, Math.floor(h * HUE_BUCKETS));
            weight[bucket] += s;
            hueSum[bucket] += h * s;
            satSum[bucket] += s * s;
        }

        let best = -1;
        for (let i = 0; i < HUE_BUCKETS; i++) {
            if (best === -1 || weight[i] > weight[best]) best = i;
        }
        if (best === -1 || weight[best] === 0) return FALLBACK;

        const hue = hueSum[best] / weight[best];
        const sat = Math.min(OUT_SAT_MAX, Math.max(OUT_SAT_MIN, satSum[best] / weight[best]));
        return hslToHex(hue, sat, OUT_LIGHTNESS);
    }

    function applyAccent(image) {
        const target = image.dataset.accentScope
            ? image.closest(image.dataset.accentScope) ?? document.documentElement
            : document.documentElement;
        target.style.setProperty("--mb-accent", dominantColor(image));
    }

    function watch(image) {
        if (image.complete && image.naturalWidth > 0) applyAccent(image);
        else image.addEventListener("load", () => applyAccent(image), { once: true });
    }

    document.querySelectorAll("img[data-accent-src]").forEach(watch);
})();
