using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Zharfa.Desktop;

// Thin WebView2 shell. Business logic, authorization and rendering live in the
// shared frontend and the backend API — never here.
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await AttachAsync();
    }

    private async Task AttachAsync()
    {
        // Dev default points at the Vite dev server; the deployed shell is
        // pointed at the Compose web/reverse-proxy service via ZHARFA_FRONTEND_URL.
        var frontendUrl = Environment.GetEnvironmentVariable("ZHARFA_FRONTEND_URL")
            ?? "http://localhost:5173";

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Zharfa",
            "WebView2");

        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await Dashboard.EnsureCoreWebView2Async(environment);
        Dashboard.CoreWebView2.Navigate(frontendUrl);
    }
}
