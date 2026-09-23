using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Media;

namespace QuickLook.Plugin.IsoViewer
{
    public class IsoTreeItem : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public bool IsDir { get; set; }
        public long Size { get; set; }
        public string SizeText => IsDir ? "" : IsoGameParser.FormatSize(Size);
        public ImageSource IconImage { get; set; }
        public ObservableCollection<IsoTreeItem> Children { get; } = new ObservableCollection<IsoTreeItem>();

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public partial class IsoFolderPanel : UserControl, IDisposable
    {
        private bool _disposed;
        private bool _stop;

        public IsoFolderPanel()
        {
            InitializeComponent();
            Resources.MergedDictionaries.Clear();
        }

        public IsoFolderPanel(string isoPath) : this()
        {
            LoadIso(isoPath);
        }

        public void LoadIso(string isoPath)
        {
            _stop = false;
            totalSize.Text = "Loading…";
            numFolders.Text = "";
            numFiles.Text = "";
            treeView.ItemsSource = null;

            Task.Run(() =>
            {
                try
                {
                    using (var iso = new IsoFileSystem(isoPath))
                    {
                        var rootItems = new ObservableCollection<IsoTreeItem>();
                        long totalDirs = 0, totalFiles = 0, totalBytes = 0;

                        BuildTree(iso, "", rootItems, ref totalDirs, ref totalFiles, ref totalBytes, 0);

                        Dispatcher.Invoke(() =>
                        {
                            if (_disposed) return;
                            treeView.ItemsSource = rootItems;
                            totalSize.Text = $"Total size: {IsoGameParser.FormatSize(totalBytes)}";
                            numFolders.Text = $"Folders: {totalDirs}";
                            numFiles.Text = $"Files: {totalFiles}";
                        });
                    }
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (_disposed) return;
                        totalSize.Text = "Error: " + ex.Message;
                        numFolders.Text = "";
                        numFiles.Text = "";
                        treeView.ItemsSource = null;
                    });
                }
            });
        }

        void BuildTree(IsoFileSystem iso, string path, ObservableCollection<IsoTreeItem> parent,
            ref long totalDirs, ref long totalFiles, ref long totalBytes, int depth)
        {
            if (_stop || depth > 16) return;

            foreach (var entry in iso.ListDirectory(path))
            {
                if (_stop) break;

                var name = entry.Name;
                var isDir = entry.IsDir;
                var size = entry.Size;

                ImageSource icon = null;
                try
                {
                    icon = isDir ? ShellIcon.ForFolder() : ShellIcon.ForFile(name);
                }
                catch { }

                var item = new IsoTreeItem
                {
                    Name = name,
                    IsDir = isDir,
                    Size = size,
                    IconImage = icon
                };

                if (isDir)
                {
                    totalDirs++;
                    string childPath = string.IsNullOrEmpty(path) ? name : path + "/" + name;
                    BuildTree(iso, childPath, item.Children, ref totalDirs, ref totalFiles, ref totalBytes, depth + 1);
                }
                else
                {
                    totalFiles++;
                    totalBytes += size;
                }

                parent.Add(item);
            }
        }

        public void Dispose()
        {
            _stop = true;
            _disposed = true;
        }
    }
}
