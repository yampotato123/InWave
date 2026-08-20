# 文件索引

這個專案的說明文件分散在三個地方，這裡是總目錄。

---

## 簡報（在 claude.ai，不在這個 repo 裡）

兩份是**不同用途**的文件，不是同一份的兩個版本：

| 簡報 | 給誰看 | 網址 |
|---|---|---|
| **專案簡報** | 評估者、非工程背景 —— 講產品命題、關鍵決策、可靠性 | https://claude.ai/code/artifact/c27b4ce6-0120-473b-b919-8838b61bd0a2 |
| **技術簡報** | 工程師、技術面試 —— 講資料流、時序、快取、失敗處理、效能量測 | https://claude.ai/code/artifact/ce37cd65-56c7-4f75-aa11-300e07c96ce8 |

> 兩份都是私有的，只有登入同一個帳號看得到。**換電腦時它們不需要搬** ——
> 用同一個帳號登入 claude.ai 就在（也可以在 claude.ai/code/artifacts 列出全部）。

---

## 決策與過程記錄（在 repo 裡）

| 檔案 | 內容 |
|---|---|
| [`../PROGRESS.md`](../PROGRESS.md) | **最重要的一份**。每個階段的決策、理由、被否決的方案、實測數據、踩過的坑 |
| [`../README.md`](../README.md) | 怎麼跑起來、金鑰怎麼放怎麼換、正式部署、常見問題 |
| [`2026-08-20-資料流與本輪改動.md`](2026-08-20-資料流與本輪改動.md) | 一張照片從進入系統到變成歌單，每一站產生什麼；以及那一輪的改動記錄 |
| [`2026-08-20-codereview-與使用者回報修正.md`](2026-08-20-codereview-與使用者回報修正.md) | 兩份獨立 code review 的六個發現，加上使用者實測回報的八項 |
| [`2026-08-20-流程時序圖.mmd`](2026-08-20-流程時序圖.mmd) | 主流程時序圖。冷路徑、熱路徑、AI 失敗回落畫在同一張 |

### 設計文件（當時的規劃，保留原貌）

| 檔案 | 內容 |
|---|---|
| [`superpowers/specs/2026-08-11-docker-n8n-ai-design.md`](superpowers/specs/2026-08-11-docker-n8n-ai-design.md) | 階段 3 的完整設計，含 spike 推翻原設計的過程與數據 |
| [`superpowers/specs/2026-08-11-codebase-survey.md`](superpowers/specs/2026-08-11-codebase-survey.md) | 容器化之前的全專案掃描 |
| [`superpowers/specs/2026-08-06-photo-editor-design.md`](superpowers/specs/2026-08-06-photo-editor-design.md) | canvas 修圖頁的設計 |

> 設計文件是**特定時間點的記錄**，不會回頭修改。實際做出來與當初規劃不同的地方，
> 差異寫在 PROGRESS.md 裡（例如 YouTube 快取沒有照計畫用 `Songs` 表）。

---

## 怎麼看時序圖

`.mmd` 是 Mermaid 原始碼，三種看法：

- **VS Code** — 裝 Markdown Preview Mermaid Support
- **GitHub** — 貼進 markdown 的 ` ```mermaid ` 區塊會自動渲染
- **技術簡報 §04** — 已經內嵌在裡面

---

## 給要報告的人：素材在哪

| 想講什麼 | 去哪找 |
|---|---|
| 為什麼這樣設計、被否決了什麼 | `PROGRESS.md` 的各階段「設計決定」段落 |
| 效能問題怎麼查出來的 | 技術簡報 §02、§11；`2026-08-20-資料流與本輪改動.md` 的量測記錄 |
| 失敗處理與實測 | 技術簡報 §08；PROGRESS 的「階段 3 步驟 5」 |
| code review 找到什麼 | `2026-08-20-codereview-與使用者回報修正.md` |
| 工具選型的理由 | 技術簡報 §01；PROGRESS 的「三個關鍵決策」 |
