using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace LoLHelper.Core.Services
{
    public class PenguService
    {
        private readonly string _lolDirectory;
        private readonly string _pluginsDirectory;

        public PenguService(string lolDirectory = @"C:\Riot Games\League of Legends")
        {
            _lolDirectory = lolDirectory;
            _pluginsDirectory = Path.Combine(_lolDirectory, "plugins");
        }

        public bool IsPenguInstalled()
        {
            // Pengu Loader typically relies on a modified core or a plugins folder.
            return Directory.Exists(_pluginsDirectory);
        }

        public async Task InstallElainaV4Async(string? customImagePath = null)
        {
            if (!Directory.Exists(_pluginsDirectory))
            {
                Directory.CreateDirectory(_pluginsDirectory);
            }

            string elainaDir = Path.Combine(_pluginsDirectory, "elaina-theme");
            if (!Directory.Exists(elainaDir))
            {
                Directory.CreateDirectory(elainaDir);
            }

            string indexJsPath = Path.Combine(elainaDir, "index.js");
            string styleCssPath = Path.Combine(elainaDir, "style.css");

            string bgUrl = "https://i.imgur.com/example-bg.jpg"; // Default fallback

            // If user provided a custom image, copy it into the plugin folder
            if (!string.IsNullOrEmpty(customImagePath) && File.Exists(customImagePath))
            {
                string ext = Path.GetExtension(customImagePath);
                string newFileName = "custom-bg" + ext;
                string destPath = Path.Combine(elainaDir, newFileName);
                
                File.Copy(customImagePath, destPath, overwrite: true);
                
                // Pengu Loader maps the plugins directory to the //plugins/ network path in the client
                bgUrl = $"//plugins/elaina-theme/{newFileName}";
                
                // Note: Windows paths use backslashes, but URLs need forward slashes. 
                // Using //plugins/ is the standard Pengu Loader asset routing.
            }

            string jsContent = @"
// Custom Theme Bootstrapper (ElainaV4 Style)
import './style.css';
console.log('Custom Theme Loaded via LoL Helper!');
";

            // CSS that overrides the main client background
            string cssContent = $@"
:root {{
    --background-url: url('{bgUrl}');
}}
.rcp-fe-viewport-root {{
    background-image: var(--background-url) !important;
    background-size: cover !important;
    background-position: center !important;
}}
.rcp-fe-lol-navigation {{
    background: rgba(0, 0, 0, 0.5) !important;
    backdrop-filter: blur(10px);
}}
";
            await File.WriteAllTextAsync(indexJsPath, jsContent);
            await File.WriteAllTextAsync(styleCssPath, cssContent);
        }
    }
}
