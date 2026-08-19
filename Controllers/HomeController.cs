using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using InWave.Models;

namespace InWave.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    // Error 由 Program.cs 的 UseExceptionHandler("/Home/Error") 使用。
    // (樣板附的 Privacy 頁沒有任何連結指向它,已於 2026-08-20 連同 view 一起刪除。)
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
