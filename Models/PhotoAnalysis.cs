namespace InWave.Models;

/// <summary>
/// 一張照片的 AI 判讀結果。與 Photo 一對一。
///
/// 刻意採「混合」形狀:常用的三欄拆出來,其餘留在 RawJson。
/// 全部拆欄位的話,AI 回傳格式一改就要一次 migration(moodPick 就是 2026-08-17 才加的);
/// 全部塞 RawJson 的話,連顯示 Scene 都得整包 parse。真的需要查詢時再拆。
/// </summary>
public class PhotoAnalysis
{
    public int Id { get; set; }
    public int PhotoId { get; set; }

    /// <summary>n8n 回傳的整包原文。格式還會變,存原文最有彈性。</summary>
    public string RawJson { get; set; } = "";

    /// <summary>AI 對照片的一句話描述,顯示「AI 怎麼理解這張照片」用。</summary>
    public string? Scene { get; set; }

    /// <summary>判讀時間。要不要重跑看這個。</summary>
    public DateTime AnalyzedAt { get; set; }

    /// <summary>用哪個模型判的,日後比較模型版本用。由工作流標註(見 scripts/build-n8n-workflow.ps1)。</summary>
    public string? ModelUsed { get; set; }

    public Photo Photo { get; set; } = null!;
}
