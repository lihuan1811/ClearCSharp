using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZyperWin__
{
    internal sealed class BusyAnimationOverlay : Control
    {
        private readonly Timer timer = new Timer();
        private readonly bool animationEnabled;
        private string title = "正在处理";
        private string message = "请稍候...";
        private int angle;
        private float phase;
        private Control host;

        public bool IsActive { get; private set; }

        public BusyAnimationOverlay()
        {
            Size = new Size(500, 104);
            Visible = false;
            TabStop = false;
            animationEnabled = SystemInformation.ClientAreaAnimation;
            timer.Interval = 33;
            timer.Tick += delegate
            {
                angle = (angle + 9) % 360;
                phase += 0.025F;
                if (phase > 1F) phase -= 1F;
                Invalidate();
            };
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
        }

        public void AttachTo(Control parent)
        {
            host = parent;
            parent.Controls.Add(this);
            parent.Resize += delegate { CenterOnHost(); };
            CenterOnHost();
        }

        public void Start(string activityTitle, string activityMessage)
        {
            title = string.IsNullOrWhiteSpace(activityTitle) ? "正在处理" : activityTitle.Trim();
            message = string.IsNullOrWhiteSpace(activityMessage) ? "请稍候..." : activityMessage.Trim();
            angle = 0;
            phase = 0F;
            IsActive = true;
            CenterOnHost();
            Visible = true;
            BringToFront();
            if (animationEnabled) timer.Start();
            Invalidate();
            Update();
        }

        public void UpdateMessage(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            message = value.Trim();
            if (IsActive) Invalidate();
        }

        public void Stop()
        {
            timer.Stop();
            IsActive = false;
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle shadow = new Rectangle(3, 4, Width - 7, Height - 8);
            using (GraphicsPath shadowPath = RoundedRectangle(shadow, 7))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(35, 24, 39, 33)))
                e.Graphics.FillPath(shadowBrush, shadowPath);

            Rectangle surface = new Rectangle(1, 1, Width - 7, Height - 8);
            using (GraphicsPath surfacePath = RoundedRectangle(surface, 7))
            using (var background = new SolidBrush(Color.White))
            using (var border = new Pen(AppPalette.Border, 1F))
            {
                e.Graphics.FillPath(background, surfacePath);
                e.Graphics.DrawPath(border, surfacePath);
            }

            Rectangle spinner = new Rectangle(24, 23, 38, 38);
            using (var trackPen = new Pen(Color.FromArgb(224, 236, 230), 4F))
            using (var spinnerPen = new Pen(AppPalette.Green, 4F))
            {
                trackPen.StartCap = trackPen.EndCap = LineCap.Round;
                spinnerPen.StartCap = spinnerPen.EndCap = LineCap.Round;
                e.Graphics.DrawArc(trackPen, spinner, 0, 360);
                e.Graphics.DrawArc(spinnerPen, spinner, animationEnabled ? angle : -70, 235);
            }

            string animatedTitle = title;
            if (animationEnabled)
                animatedTitle += new string('.', 1 + ((int)(phase * 12) % 3));
            Rectangle titleBounds = new Rectangle(80, 17, Width - 108, 28);
            Rectangle messageBounds = new Rectangle(80, 43, Width - 108, 24);
            TextRenderer.DrawText(e.Graphics, animatedTitle, UiFactory.SectionFont, titleBounds, AppPalette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(e.Graphics, message, UiFactory.BaseFont, messageBounds, AppPalette.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            int trackX = 80;
            int trackY = Height - 25;
            int trackWidth = Width - 108;
            using (var track = new Pen(Color.FromArgb(225, 235, 230), 3F))
            using (var scan = new Pen(AppPalette.Green, 3F))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                scan.StartCap = scan.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, trackX, trackY, trackX + trackWidth, trackY);
                float position = animationEnabled ? phase : 0.45F;
                int segment = Math.Max(48, trackWidth / 5);
                int start = trackX + (int)((trackWidth + segment) * position) - segment;
                int left = Math.Max(trackX, start);
                int right = Math.Min(trackX + trackWidth, start + segment);
                if (right > left) e.Graphics.DrawLine(scan, left, trackY, right, trackY);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }

        private void CenterOnHost()
        {
            if (host == null) return;
            int width = Math.Min(500, Math.Max(320, host.ClientSize.Width - 48));
            Size = new Size(width, 104);
            Location = new Point(
                Math.Max(0, (host.ClientSize.Width - Width) / 2),
                Math.Max(8, (host.ClientSize.Height - Height) / 2));
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
