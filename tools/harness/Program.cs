using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ThemeForge.Models;
using ThemeForge.ViewModels;

namespace Harness
{
    /// <summary>
    /// Offline render harness. Hosts the plugin views with hand built data so the xaml,
    /// the editor templates and the preview surface can be inspected without Playnite.
    /// Not shipped with the add-on.
    /// </summary>
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            var outputDir = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
            var width = args.Length > 2 ? double.Parse(args[2]) : 1220d;
            var height = args.Length > 3 ? double.Parse(args[3]) : 820d;
            var suffix = args.Length > 4 ? args[4] : "";
            var app = new Application();
            app.Resources.MergedDictionaries.Add(FakeTheme.Build());

            var locFolder = AppDomain.CurrentDomain.BaseDirectory;
            ThemeForge.Localization.Load(locFolder, args.Length > 1 ? args[1] : "zh_CN");

            var view = new ThemeForge.Views.SettingsView();
            view.DataContext = FakeViewModel.Build();

            var window = new Window
            {
                Width = width,
                Height = height,
                Title = "Theme Forge harness",
                Content = new System.Windows.Controls.Border { Padding = new Thickness(10), Child = view },
                Background = (Brush)app.Resources["WindowBackgourndBrush"],
                Foreground = (Brush)app.Resources["TextBrush"],
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Left = -4000,
                Top = -4000
            };

            var shots = new List<string>();
            window.ContentRendered += (s, e) =>
            {
                try
                {
                    var tabs = FindTabControl(view);
                    for (var i = 0; i < (tabs == null ? 1 : tabs.Items.Count); i++)
                    {
                        if (tabs != null)
                        {
                            tabs.SelectedIndex = i;
                        }

                        window.UpdateLayout();
                        Pump();
                        shots.Add(Snap(window, Path.Combine(outputDir, "harness" + suffix + "-tab" + i + ".png")));
                    }
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(outputDir, "harness-error.txt"), ex.ToString());
                }
                finally
                {
                    File.WriteAllText(Path.Combine(outputDir, "harness-shots.txt"), string.Join(Environment.NewLine, shots));
                    File.WriteAllText(Path.Combine(outputDir, "harness" + suffix + "-layout.txt"), Report(view));
                    app.Shutdown();
                }
            };

            app.Run(window);
        }

        /// <summary>
        /// Walks the visual tree and flags every element whose desired width exceeds the slot it
        /// was given. That mismatch is exactly what silently clips content in a fixed size window.
        /// </summary>
        private static double Limit;

        private static double Right(FrameworkElement element)
        {
            try
            {
                var point = element.TransformToAncestor(Root).Transform(new Point(element.ActualWidth, 0));
                return point.X;
            }
            catch
            {
                return 0;
            }
        }

        private static Visual Root;

        private static string Report(FrameworkElement root)
        {
            Root = root;
            Limit = root.ActualWidth;
            var sink = new System.Text.StringBuilder();
            Walk(root, 0, sink);
            return sink.ToString();
        }

        private static void Walk(DependencyObject node, int depth, System.Text.StringBuilder sink)
        {
            var element = node as FrameworkElement;
            if (element != null && element.ActualWidth > 0)
            {
                if (Right(element) - Limit > 0.6)
                {
                    sink.AppendLine(new string((char)32, depth * 2)
                        + element.GetType().Name
                        + " name=" + (element.Name.Length == 0 ? "-" : element.Name)
                        + " right=" + Right(element).ToString("0.0")
                        + " width=" + element.ActualWidth.ToString("0.0"));
                }
            }

            var count = VisualTreeHelper.GetChildrenCount(node);
            for (var i = 0; i < count; i++)
            {
                Walk(VisualTreeHelper.GetChild(node, i), depth + 1, sink);
            }
        }

        /// <summary>Lets the dispatcher finish layout and template application before capturing.</summary>
        private static void Pump()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static System.Windows.Controls.TabControl FindTabControl(DependencyObject root)
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var tabs = child as System.Windows.Controls.TabControl;
                if (tabs != null)
                {
                    return tabs;
                }

                var nested = FindTabControl(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static string Snap(Window window, string file)
        {
            var bitmap = new RenderTargetBitmap(
                (int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(file))
            {
                encoder.Save(stream);
            }

            return file;
        }
    }
}
