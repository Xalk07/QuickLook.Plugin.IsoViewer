using System.Windows;
using System.Windows.Controls;
using QuickLook.Common.Plugin;

namespace QuickLook.Plugin.IsoViewer
{
    public partial class PspIsoViewer : UserControl
    {
        public PspIsoViewer()
        {
            InitializeComponent();
        }

        public PspIsoViewer(ContextObject context, string isoPath, IsoGameInfo info) : this()
        {
            // Создаём ViewerPane с контекстом темы
            var pane = new ViewerPane(context) { Info = info };

            var grid = (Grid)Content;
            // Заменяем placeholder
            if (grid.Children.Count > 0 && grid.Children[0] is ViewerPane)
            {
                grid.Children.RemoveAt(0);
            }
            Grid.SetRow(pane, 0);
            grid.Children.Insert(0, pane);

            folderPane.LoadIso(isoPath);
        }
    }
}
