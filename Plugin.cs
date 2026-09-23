using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.IsoViewer
{
    public class Plugin : IViewer
    {
        public int Priority => 10;

        public void Init() { }

        public bool CanHandle(string path)
        {
            if (string.IsNullOrEmpty(path) || Directory.Exists(path))
                return false;

            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".iso";
        }

        public void Prepare(string path, ContextObject context)
        {
            context.PreferredSize = new Size(920, 720);
            context.Title = Path.GetFileName(path);
        }

        public void View(string path, ContextObject context)
        {
            try
            {
                var info = IsoGameParser.Parse(path);

                if (info != null && info.IsPsp)
                {
                    // PSP ISO → комбинированный вид: сверху метаданные/картинки/звук, снизу дерево
                    context.ViewerContent = new PspIsoViewer(context, path, info);
                }
                else
                {
                    // Обычный ISO → только дерево файлов
                    context.ViewerContent = new IsoFolderPanel(path);
                }
            }
            catch (Exception ex)
            {
                context.ViewerContent = new Label
                {
                    Content = "Не удалось прочитать ISO\n" + ex.Message,
                    FontSize = 14,
                    Foreground = System.Windows.Media.Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };
            }

            context.IsBusy = false;
        }

        public void Cleanup() { }
    }
}
