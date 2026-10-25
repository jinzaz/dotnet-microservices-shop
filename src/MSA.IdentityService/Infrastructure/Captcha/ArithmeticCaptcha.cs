using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MSA.Redis;
using StackExchange.Redis;

namespace MSA.IdentityService.Infrastructure.Captcha;

/// <summary>图形验证码：前端用 &lt;img&gt; 显示 data URI，答案只存在 Redis。</summary>
public sealed record CaptchaResponse(string CaptchaId, string Image, int ExpiresInSeconds);

/// <summary>
/// 答案存 Redis 两分钟，一个验证码只能试一次（取出即删），答错前端换一张。
/// Redis 不可用时放行并记警告，和 IdentityThrottle 一致：人机验证只是减速带，号码级冷却和小时上限仍在数据库里生效。
/// </summary>
public sealed class CaptchaService(IConnectionMultiplexer redis, RedisKeyspace keyspace, ILogger<CaptchaService> logger)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    public async Task<CaptchaResponse> CreateAsync()
    {
        var (svg, answer) = ArithmeticCaptcha.Create();
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        try
        {
            await redis.GetDatabase().StringSetAsync(Key(id), answer, Lifetime);
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Captcha {CaptchaId} was not stored: Redis is unavailable.", id);
        }

        return new CaptchaResponse(
            id,
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg)),
            (int)Lifetime.TotalSeconds);
    }

    public async Task<bool> VerifyAsync(string captchaId, string answer)
    {
        try
        {
            var expected = await redis.GetDatabase().StringGetDeleteAsync(Key(captchaId));
            return !expected.IsNull
                && int.TryParse(answer, NumberStyles.None, CultureInfo.InvariantCulture, out var actual)
                && (int)expected == actual;
        }
        catch (RedisException exception)
        {
            logger.LogWarning(exception, "Captcha check skipped: Redis is unavailable.");
            return true;
        }
    }

    private RedisKey Key(string captchaId) => keyspace.Key($"captcha:{captchaId}");
}

/// <summary>
/// 本地算术图形验证码：手写笔画字形 + 随机缩放、旋转、抖动 + 干扰曲线和噪点，输出 SVG。
/// 路径顺序打乱，不按字符顺序排列。
/// ponytail: 只是减速带——字形是固定的，专门写脚本解析 SVG 路径能破；真被刷时换第三方行为验证（如阿里云验证码 2.0）。
/// </summary>
internal static class ArithmeticCaptcha
{
    private const float Width = 150;
    private const float Height = 48;
    private static readonly string[] InkColors = ["#e5e5e5", "#c5ff4a", "#c5c5c5", "#a8d8ff"];
    private static readonly string[] NoiseColors = ["#525252", "#7a7a7a", "#597321"];

    // 10×16 网格（y 向下）上的笔画，每个字形由若干条折线组成。
    private static readonly Dictionary<char, (float X, float Y)[][]> Glyphs = new()
    {
        ['0'] = [[(2, 0), (8, 0), (10, 3), (10, 13), (8, 16), (2, 16), (0, 13), (0, 3), (2, 0)]],
        ['1'] = [[(2, 4), (6, 0), (6, 16)], [(2, 16), (10, 16)]],
        ['2'] = [[(0, 4), (2, 0), (8, 0), (10, 3), (10, 6), (0, 16), (10, 16)]],
        ['3'] = [[(0, 1), (10, 0), (5, 7), (8, 8), (10, 11), (9, 15), (5, 16), (0, 15)]],
        ['4'] = [[(7, 16), (7, 0), (0, 11), (10, 11)]],
        ['5'] = [[(10, 0), (1, 0), (0, 7), (6, 6), (10, 9), (10, 13), (7, 16), (0, 15)]],
        ['6'] = [[(9, 1), (5, 0), (1, 4), (0, 10), (1, 15), (5, 16), (9, 15), (10, 11), (8, 8), (4, 8), (0, 11)]],
        ['7'] = [[(0, 0), (10, 0), (4, 16)]],
        ['8'] = [[(5, 8), (1, 6), (1, 2), (4, 0), (6, 0), (9, 2), (9, 6), (5, 8), (1, 10), (0, 14), (3, 16), (7, 16), (10, 14), (9, 10), (5, 8)]],
        ['9'] = [[(10, 5), (8, 8), (2, 8), (0, 5), (1, 1), (5, 0), (9, 1), (10, 5), (9, 12), (5, 16), (1, 15)]],
        ['+'] = [[(5, 3), (5, 13)], [(0, 8), (10, 8)]],
        ['-'] = [[(1, 8), (9, 8)]],
        ['×'] = [[(1, 4), (9, 12)], [(9, 4), (1, 12)]],
        ['='] = [[(0, 5), (10, 5)], [(0, 11), (10, 11)]],
        ['?'] = [[(1, 4), (2, 1), (5, 0), (8, 1), (9, 4), (5, 8), (5, 11)], [(5, 14), (5, 16)]]
    };

    public static (string Svg, int Answer) Create()
    {
        var (text, answer) = Expression();
        var paths = new List<string>();

        // 按字符数均分宽度，最长 "20+20=?" 也放得下。
        var step = (Width - 16) / text.Length;
        for (var index = 0; index < text.Length; index++)
        {
            var scale = Math.Min(1.7f, (step - 3) / 10) * Next(0.88f, 1.04f);
            var angle = Next(-0.3f, 0.3f);
            var (sin, cos) = MathF.SinCos(angle);
            var centerX = 8 + step * (index + 0.5f) + Next(-1.5f, 1.5f);
            var centerY = Height / 2 + Next(-3, 3);
            var color = Pick(InkColors);
            var strokeWidth = Next(1.8f, 2.6f);

            foreach (var stroke in Glyphs[text[index]])
            {
                var path = new StringBuilder();
                for (var point = 0; point < stroke.Length; point++)
                {
                    var x = (stroke[point].X - 5 + Next(-0.5f, 0.5f)) * scale;
                    var y = (stroke[point].Y - 8 + Next(-0.5f, 0.5f)) * scale;
                    path.Append(point == 0 ? 'M' : 'L')
                        .Append(Format(centerX + x * cos - y * sin)).Append(' ')
                        .Append(Format(centerY + x * sin + y * cos)).Append(' ');
                }
                paths.Add(Path(path.ToString(), color, strokeWidth));
            }
        }

        for (var index = 0; index < 4; index++)
        {
            var curve = $"M{Format(Next(0, 20))} {Format(Next(6, Height - 6))} " +
                $"C{Format(Next(30, 60))} {Format(Next(0, Height))} {Format(Next(90, 120))} {Format(Next(0, Height))} " +
                $"{Format(Next(Width - 20, Width))} {Format(Next(6, Height - 6))}";
            // 比字形笔画细，不然横穿中间的曲线会和减号、等号混淆。
            paths.Add(Path(curve, Pick(index % 2 == 0 ? InkColors : NoiseColors), Next(0.7f, 1.2f)));
        }
        for (var index = 0; index < 24; index++)
        {
            paths.Add($"<circle cx=\"{Format(Next(0, Width))}\" cy=\"{Format(Next(0, Height))}\" " +
                $"r=\"{Format(Next(0.6f, 1.4f))}\" fill=\"{Pick(NoiseColors)}\"/>");
        }
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paths));

        var svg = new StringBuilder()
            .Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{Width}\" height=\"{Height}\" viewBox=\"0 0 {Width} {Height}\">")
            .Append("<rect width=\"100%\" height=\"100%\" fill=\"#141414\"/>")
            .AppendJoin(string.Empty, paths)
            .Append("</svg>");
        return (svg.ToString(), answer);
    }

    private static (string Text, int Answer) Expression()
    {
        switch (RandomNumberGenerator.GetInt32(3))
        {
            case 0:
            {
                var (a, b) = (RandomNumberGenerator.GetInt32(1, 21), RandomNumberGenerator.GetInt32(1, 21));
                return ($"{a}+{b}=?", a + b);
            }
            case 1:
            {
                var (a, b) = (RandomNumberGenerator.GetInt32(10, 31), RandomNumberGenerator.GetInt32(1, 10));
                return ($"{a}-{b}=?", a - b);
            }
            default:
            {
                var (a, b) = (RandomNumberGenerator.GetInt32(2, 10), RandomNumberGenerator.GetInt32(2, 10));
                return ($"{a}×{b}=?", a * b);
            }
        }
    }

    private static string Path(string data, string color, float width) =>
        $"<path d=\"{data.TrimEnd()}\" fill=\"none\" stroke=\"{color}\" stroke-width=\"{Format(width)}\" " +
        "stroke-linecap=\"round\" stroke-linejoin=\"round\"/>";

    private static float Next(float min, float max) => min + Random.Shared.NextSingle() * (max - min);

    private static string Pick(string[] values) => values[Random.Shared.Next(values.Length)];

    private static string Format(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
