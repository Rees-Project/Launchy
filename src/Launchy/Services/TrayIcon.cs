using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Launchy.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly CommandStore _store;

    public event EventHandler? OpenRequested;
    public event EventHandler? EditRequested;
    public event EventHandler? ExitRequested;

    public TrayIcon(CommandStore store)
    {
        _store = store;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Launchy", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Edit commands…", null, (_, _) => EditRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Open commands file (JSON)", null, (_, _) => OpenCommandsFile());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new Forms.NotifyIcon
        {
            Icon = CreateLogoIcon(),
            Text = "Launchy (Alt+Q)",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                OpenRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    private void OpenCommandsFile()
    {
        if (!File.Exists(_store.CommandsPath))
        {
            _store.Load();
        }

        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_store.CommandsPath}\"") { UseShellExecute = true });
    }

    /// <summary>
    /// Renders the logo's arrow "L" into a 32 px icon, so the tray always matches the in-app logo.
    /// </summary>
    private static System.Drawing.Icon CreateLogoIcon()
    {
        const int size = 32;
        var geometry = (Geometry)Application.Current.FindResource("LogoArrowGeometry");
        var brush = (Brush)Application.Current.FindResource("BrandGradient");

        var bounds = geometry.Bounds;
        var scale = (size - 2) / Math.Max(bounds.Width, bounds.Height);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new TranslateTransform((size - bounds.Width * scale) / 2, (size - bounds.Height * scale) / 2));
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawGeometry(brush, new Pen(brush, 1) { LineJoin = PenLineJoin.Round }, geometry);
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var png = new MemoryStream();
        encoder.Save(png);

        // Wrap the PNG in a single-image .ico container.
        using var ico = new MemoryStream();
        using (var writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((short)0);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((short)1);
            writer.Write((short)32);
            writer.Write((int)png.Length);
            writer.Write(22);
            writer.Write(png.ToArray());
        }

        ico.Position = 0;
        return new System.Drawing.Icon(ico);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
