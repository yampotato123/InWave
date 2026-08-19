# 產生 n8n 工作流 inwave-analyze 的定義檔（階段 3 步驟 1）。
#
# 這支腳本是工作流的「原始碼」：JSON 是它的產物，改流程請改這裡再重跑。
# 之所以用腳本產生而不是在 n8n UI 手點，是因為 UI 改動無法 code review、
# 也無法從 git 看出改了什麼（2026-08-17 的半成品就是這樣沒人發現 httpMethod 漏設）。
#
# 用法：
#   .\scripts\build-n8n-workflow.ps1
#   docker cp .\n8n-workflows\inwave-analyze.json n8n:/tmp/wf.json
#   docker exec n8n n8n import:workflow --input=/tmp/wf.json
#   docker exec n8n n8n update:workflow --id=inwaveAnalyze001 --active=true
#   docker restart n8n        # 必要：webhook 註冊表在記憶體，不重啟不會生效
#
# 憑證用 ID 參照（Header Auth、Google Gemini），同一個 n8n 實例內有效，
# 匯出入不會帶到金鑰本身。換機器要重建憑證並更新這裡的 ID。
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'n8n-workflows\inwave-analyze.json'

# --- 節點 2：組 prompt（純 JS 字串，比塞在 Gemini 欄位的 expression 好維護；見筆記 §2 末） ---
$buildCode = @'
const b = $input.first().json.body ?? {};

const base = `你是音樂推薦系統。看這張照片,判斷它的氣氛,然後推薦真實存在的歌曲。

只回傳 JSON,不要其他文字:
{
  "scene": "一句話描述照片,15 字以內",
  "moodPick": "從八個詞選一個最貼近的",
  "mood": ["3-5 個氣氛形容詞,用中文"],
  "keywords": ["3-5 個備用搜尋詞,用英文"],
  "songs": [
    {"artist": "歌手", "title": "歌名", "why": "為什麼配這張照片,15 字內"}
  ]
}

規則:
- songs 給 5 首,必須是「真實存在且 YouTube 上找得到」的歌
- 挑有一定知名度的,不要冷門到搜不到
- 五首的曲風要有變化,不要全部同一種
- 五首中至少 1 首是純音樂(演奏曲/配樂),其餘可有人聲
- 五首中最多 1-2 首可以是動畫/遊戲/電影/影集的 OST,且作品要有知名度
- artist / title 用該曲原本的語言寫
- 不確定某首歌是否存在時,換一首你確定的
- keywords 是 songs 全部找不到時的備案,避免 lofi / radio / mix / 24/7 這類詞
- mood 用中文,keywords 用英文
- moodPick 必須剛好是這八個詞的其中一個,不可自創、不可留空:
  夜色 午後 溫暖 懷舊 夢幻 安靜 活力 慶祝
  mood 陣列不受此限制,可自由發揮`;

let p = base;

// 條件段 A：使用者有指定情緒才附加（沒選就整段不送，AI 無從被錨定）
if (b.mood) {
  p += `

使用者已明確指定這張照片的情緒為「${b.mood}」。這是他的意圖,請以此為準。
若照片內容與該情緒有落差,仍以使用者的指定優先,但在 scene 中如實描述照片。
此時 moodPick 直接回傳「${b.mood}」。`;
}

// 條件段 B：使用者有套濾鏡才附加
if (b.filter) {
  p += `

使用者為這張照片選擇的風格是「${b.filter}」,請讓推薦帶上這個風格的色彩。`;
}

// 條件段 C：滑桿偏離預設 100 才附加（全預設等於送雜訊）
const s = b.sliders ?? {};
const labels = { brightness: '亮度', contrast: '對比', saturation: '飽和度' };
const diffs = Object.keys(labels)
  .filter(k => typeof s[k] === 'number' && s[k] !== 100)
  .map(k => `${labels[k]}${s[k] > 100 ? '調高' : '調低'}到 ${s[k]}%`);
if (diffs.length) {
  p += `

使用者調整了照片:${diffs.join('、')}。`;
}

return [{ json: { body: b, prompt: p } }];
'@

# --- 節點 5：parse（照筆記 §1 節點 4 原文，不吞錯） ---
$parseCode = @'
// 不同供應商的回應形狀不同,依序試。多留幾條退路的成本很低,
// 而猜錯的代價是「模型明明回答了,我們卻當成沒有」。
const item = $input.first().json;

const openAiText = Array.isArray(item?.output)
  ? item.output
      .flatMap(o => Array.isArray(o?.content) ? o.content : [])
      .map(c => c?.text)
      .find(t => typeof t === 'string' && t.length > 0)
  : undefined;

const raw =
  openAiText                          // OpenAI Responses API(simplify 關掉時的完整回應)
  ?? item?.content?.parts?.[0]?.text  // Gemini
  ?? item?.output_text
  ?? item?.text
  ?? '';

// 去掉 ```json ... ``` 圍欄
const cleaned = raw
  .replace(/^\s*```(?:json)?\s*/i, '')
  .replace(/```\s*$/, '')
  .trim();

try {
  // ok 與 model 都放在展開之後,AI 覆寫不了。
  // 先前寫成 { ok: true, ...JSON.parse(cleaned) },AI 只要自己回一個 "ok" 欄位
  // 就能把成功旗標改掉——最不可信的輸入決定了成敗判準,那是反過來的。
  // model:n8n 的 Gemini 節點輸出只有 content / finishReason / index,拿不到 modelVersion,
  // 所以由工作流標註,值來自產生本檔的 $modelId,與節點設定同一個來源。
  return [{ json: { ...JSON.parse(cleaned), ok: true, model: '__MODEL_ID__' } }];
} catch (e) {
  // 不吞掉——把原文回傳,C# 端才知道是 AI 格式壞掉而非網路問題,並據此走 fallback
  return [{ json: { ok: false, error: 'PARSE_FAILED', raw } }];
}
'@

# 2026-08-20 由 Gemini 改為 OpenAI(使用者購買了 OpenAI 額度)。
# 這個變數同時餵給節點設定與 Parse 節點回傳的 model 欄位,只有一個來源。
#
# gpt-4o 是 n8n 這顆節點自己的預設值,拿來當起點。要換模型改這一行就好,
# 或在 n8n UI 的 Model 下拉選(那裡列的是你帳號實際可用的清單)。
$modelId = 'gpt-4o'
$parseCode = $parseCode.Replace('__MODEL_ID__', $modelId)

$wf = [ordered]@{
  id     = 'inwaveAnalyze001'
  name   = 'inwave-analyze'
  active = $false
  nodes  = @(
    [ordered]@{
      parameters = [ordered]@{
        httpMethod     = 'POST'
        path           = 'inwave/analyze'
        authentication = 'headerAuth'
        responseMode   = 'responseNode'
        options        = @{}
      }
      type        = 'n8n-nodes-base.webhook'
      typeVersion = 2.1
      position    = @(0, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Webhook'
      webhookId   = [guid]::NewGuid().ToString()
      credentials = @{ httpHeaderAuth = [ordered]@{ id = 'GJ2pb23Tkekz89xL'; name = 'Header Auth account' } }
    },
    [ordered]@{
      parameters  = [ordered]@{ jsCode = $buildCode }
      type        = 'n8n-nodes-base.code'
      typeVersion = 2
      position    = @(220, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Build Prompt'
    },
    [ordered]@{
      parameters = [ordered]@{
        operation      = 'toBinary'
        sourceProperty = 'body.imageBase64'
        options        = [ordered]@{ mimeType = "={{ `$('Build Prompt').item.json.body.mimeType || 'image/jpeg' }}" }
      }
      type        = 'n8n-nodes-base.convertToFile'
      typeVersion = 1.1
      position    = @(440, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Convert to File'
    },
    [ordered]@{
      parameters = [ordered]@{
        resource  = 'image'
        operation = 'analyze'
        # resourceLocator:用 id 模式給純字串,不需要 cachedResultName,匯出入也不會走樣
        modelId   = [ordered]@{ '__rl' = $true; value = $modelId; mode = 'id' }
        text      = "={{ `$('Build Prompt').item.json.prompt }}"
        # 預設是 'url'(要圖片網址)。我們送的是 Convert to File 產生的 binary,
        # 欄位名 data——這兩個不改會直接失敗。
        inputType          = 'base64'
        binaryPropertyName = 'data'
        # simplify 預設 true,只回 response.output(陣列)。關掉拿完整回應,
        # Parse 節點才有穩定的路徑可走。
        simplify  = $false
        options   = [ordered]@{
          # **預設只有 300**。我們要的 JSON(場景 + 五首歌 + 兩個陣列)會被截斷,
          # 然後 Parse 失敗回 PARSE_FAILED——症狀看起來像模型不聽話,其實是被切掉。
          maxTokens = 1000
        }
      }
      type        = '@n8n/n8n-nodes-langchain.openAi'
      typeVersion = 2.3
      position    = @(660, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Analyze an image'
      credentials = @{ openAiApi = [ordered]@{ id = 'GtmEBsimCNj1bE2S'; name = 'OpenAI InWave' } }
      # 供應商暫時性故障時重試一次;仍失敗就走錯誤輸出,絕不讓 webhook 回 200 空 body。
      #
      # 為什麼從 3 次降到 2 次:n8n 的重試**不能依狀態碼區分**。額度或速率問題(429)
      # 重試只會燒更快、讓使用者多等一倍——2026-08-19 Gemini 那晚就是這樣加速燒完的。
      # 真要條件式重試,得把呼叫改寫進 Code 節點自己打 HTTP,目前不值得。
      retryOnFail      = $true
      maxTries         = 2
      waitBetweenTries = 3000
      onError          = 'continueErrorOutput'
    },
    [ordered]@{
      parameters  = [ordered]@{ jsCode = @'
// Gemini 重試後仍失敗才會走到這裡。回明確的錯誤碼，C# 據此走 MoodKeywordMapper fallback。
// n8n 錯誤輸出的形狀不固定（有時 json.error 是字串，有時是物件），三種都接。
const item = $input.first();
const e = item.json ?? {};
const src = e.error ?? e.message ?? item.error ?? '';
const detail = (typeof src === 'string' ? src : (src.message ?? src.description ?? JSON.stringify(src)))
  .toString().slice(0, 300);
return [{ json: { ok: false, error: 'AI_UNAVAILABLE', detail } }];
'@ }
      type        = 'n8n-nodes-base.code'
      typeVersion = 2
      position    = @(880, 200)
      id          = [guid]::NewGuid().ToString()
      name        = 'AI Unavailable'
    },
    [ordered]@{
      parameters  = [ordered]@{ jsCode = $parseCode }
      type        = 'n8n-nodes-base.code'
      typeVersion = 2
      position    = @(880, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Parse AI Response'
    },
    [ordered]@{
      parameters  = [ordered]@{ options = @{} }
      type        = 'n8n-nodes-base.respondToWebhook'
      typeVersion = 1.5
      position    = @(1100, 0)
      id          = [guid]::NewGuid().ToString()
      name        = 'Respond to Webhook'
    }
  )
  connections = [ordered]@{
    'Webhook'            = @{ main = @(, @(@{ node = 'Build Prompt';        type = 'main'; index = 0 })) }
    'Build Prompt'       = @{ main = @(, @(@{ node = 'Convert to File';     type = 'main'; index = 0 })) }
    'Convert to File'    = @{ main = @(, @(@{ node = 'Analyze an image';    type = 'main'; index = 0 })) }
    # 主輸出 → 正常 parse；錯誤輸出（index 1）→ AI Unavailable，兩條都回到 Respond
    'Analyze an image'   = @{ main = @(
        @(@{ node = 'Parse AI Response'; type = 'main'; index = 0 }),
        @(@{ node = 'AI Unavailable';    type = 'main'; index = 0 })
      ) }
    'Parse AI Response'  = @{ main = @(, @(@{ node = 'Respond to Webhook';  type = 'main'; index = 0 })) }
    'AI Unavailable'     = @{ main = @(, @(@{ node = 'Respond to Webhook';  type = 'main'; index = 0 })) }
  }
  settings = [ordered]@{ executionOrder = 'v1'; binaryMode = 'separate' }
  meta     = [ordered]@{ templateCredsSetupCompleted = $true }
  pinData  = @{}
}

$json = ConvertTo-Json @($wf) -Depth 30
[System.IO.File]::WriteAllText($out, $json, [System.Text.UTF8Encoding]::new($false))
"wrote $out ($((Get-Item $out).Length) bytes)"
