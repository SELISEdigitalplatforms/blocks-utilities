using System.Security.Cryptography;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Captcha.DomainService.Captcha
{
    public class HardCaptchaGenerator : ICaptchaGenerator
    {
        private readonly FontFamily _fontFamily;

        public HardCaptchaGenerator()
        {
            var fontCollection = new FontCollection();
            var fontPath = Path.Combine(AppContext.BaseDirectory, "Resources", "georgia.ttf");
            _fontFamily = fontCollection.Add(fontPath);
        }

        public string Generate(string captchaString)
        {
            int width = 190, height = 80;
            var random = RandomNumberGenerator.Create();
            var fontEmSizes = new int[] { 15, 20, 25, 30, 35 };

            using var image = new Image<Rgba32>(width, height);
            var backgroundColor = Color.White;
            var textColor = Color.Black;

            image.Mutate(ctx =>
            {
                ctx.Fill(backgroundColor);
                for (int i = 0; i < captchaString.Length; i++)
                {
                    int fontSize = fontEmSizes[GetRandomIndex(fontEmSizes.Length, random)];
                    var font = _fontFamily.CreateFont(fontSize);

                    float x = Math.Max(width / (captchaString.Length + 1) * i, 10);
                    float y = GetRandomInt(10, 40, random);

                    ctx.DrawText(captchaString[i].ToString(), font, textColor, new PointF(x, y));
                }
            });

            using var stream = new MemoryStream();
            image.SaveAsPng(stream);
            return Convert.ToBase64String(stream.ToArray());
        }

        private static int GetRandomIndex(int length, RandomNumberGenerator random)
        {
            byte[] randomBytes = new byte[4];
            random.GetBytes(randomBytes);
            int randomValue = BitConverter.ToInt32(randomBytes, 0) & int.MaxValue;
            return randomValue % length;
        }

        private static int GetRandomInt(int minValue, int maxValue, RandomNumberGenerator random)
        {
            byte[] randomBytes = new byte[4];
            random.GetBytes(randomBytes);
            int randomValue = BitConverter.ToInt32(randomBytes, 0) & int.MaxValue;
            return minValue + (randomValue % (maxValue - minValue));
        }
    }
}
