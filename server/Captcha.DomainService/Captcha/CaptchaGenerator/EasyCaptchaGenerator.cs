using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.Fonts;

namespace Captcha.DomainService.Captcha
{
    public class EasyCaptchaGenerator : ICaptchaGenerator
    {
        private readonly Font _font;

        public EasyCaptchaGenerator()
        {
            var fontCollection = new FontCollection();
            var fontPath = Path.Combine(AppContext.BaseDirectory, "Resources", "georgia.ttf");
            var fontFamily = fontCollection.Add(fontPath);
            _font = fontFamily.CreateFont(40);
        }

        public string Generate(string captchaString)
        {
            var width = 190;
            var height = 80;

            using var image = new Image<Rgba32>(width, height);
            image.Mutate(ctx =>
            {
                ctx.Fill(Color.WhiteSmoke);
                ctx.DrawText(captchaString, _font, Color.Black, new PointF(10, 20));
            });

            using var ms = new MemoryStream();
            image.Save(ms, new PngEncoder());
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}
