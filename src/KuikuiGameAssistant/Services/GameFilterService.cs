using System.Runtime.InteropServices;
using KuikuiGameAssistant.Models;
using KuikuiGameAssistant.Views;

namespace KuikuiGameAssistant.Services;

public sealed class GameFilterService : IDisposable
{
    private bool _magnificationInitialized;
    private GameVignetteWindow? _vignetteWindow;

    public string StatusText { get; private set; } = "滤镜未启用";

    public void Apply(GameFilterSettings settings)
    {
        var colorReady = true;
        if (settings.IsEnabled)
        {
            colorReady = ApplyColorMatrix(settings);
        }
        else
        {
            ResetColorEffect();
        }

        ApplyVignette(settings);
        StatusText = BuildStatusText(settings, colorReady);
    }

    public void Dispose()
    {
        ResetColorEffect();
        HideVignette();
        if (_magnificationInitialized)
        {
            MagUninitialize();
            _magnificationInitialized = false;
        }
    }

    private bool ApplyColorMatrix(GameFilterSettings settings)
    {
        if (!EnsureMagnification())
        {
            return false;
        }

        var matrix = BuildColorMatrix(settings);
        var effect = new MagColorEffect { Transform = matrix };
        return MagSetFullscreenColorEffect(ref effect);
    }

    private void ResetColorEffect()
    {
        if (!_magnificationInitialized)
        {
            return;
        }

        var effect = new MagColorEffect { Transform = IdentityMatrix() };
        MagSetFullscreenColorEffect(ref effect);
    }

    private bool EnsureMagnification()
    {
        if (_magnificationInitialized)
        {
            return true;
        }

        _magnificationInitialized = MagInitialize();
        return _magnificationInitialized;
    }

    private void ApplyVignette(GameFilterSettings settings)
    {
        if (!settings.VignetteEnabled)
        {
            HideVignette();
            return;
        }

        _vignetteWindow ??= new GameVignetteWindow();
        _vignetteWindow.Apply(settings);
        if (!_vignetteWindow.IsVisible)
        {
            _vignetteWindow.Show();
        }
    }

    private void HideVignette()
    {
        if (_vignetteWindow is null)
        {
            return;
        }

        _vignetteWindow.Close();
        _vignetteWindow = null;
    }

    private static float[] BuildColorMatrix(GameFilterSettings settings)
    {
        var matrix = IdentityMatrix();
        matrix = Multiply(matrix, ContrastMatrix(1 + settings.Contrast / 100d));
        matrix = Multiply(matrix, BrightnessMatrix(settings.Brightness / 100d));
        matrix = Multiply(matrix, SaturationMatrix((settings.Saturation / 100d) * (1 - settings.Grayscale / 100d)));
        matrix = Multiply(matrix, HueRotateMatrix(settings.Hue));
        return matrix;
    }

    private static float[] IdentityMatrix()
    {
        return new[]
        {
            1f, 0f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f, 0f,
            0f, 0f, 1f, 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
            0f, 0f, 0f, 0f, 1f
        };
    }

    private static float[] BrightnessMatrix(double brightness)
    {
        var matrix = IdentityMatrix();
        matrix[20] = (float)brightness;
        matrix[21] = (float)brightness;
        matrix[22] = (float)brightness;
        return matrix;
    }

    private static float[] ContrastMatrix(double contrast)
    {
        contrast = Math.Max(0, contrast);
        var translate = (1 - contrast) / 2d;
        var matrix = IdentityMatrix();
        matrix[0] = matrix[6] = matrix[12] = (float)contrast;
        matrix[20] = matrix[21] = matrix[22] = (float)translate;
        return matrix;
    }

    private static float[] SaturationMatrix(double saturation)
    {
        saturation = Math.Clamp(saturation, 0, 2);
        const double rw = 0.2126;
        const double gw = 0.7152;
        const double bw = 0.0722;
        var inverse = 1 - saturation;
        return new[]
        {
            (float)(rw * inverse + saturation), (float)(rw * inverse), (float)(rw * inverse), 0f, 0f,
            (float)(gw * inverse), (float)(gw * inverse + saturation), (float)(gw * inverse), 0f, 0f,
            (float)(bw * inverse), (float)(bw * inverse), (float)(bw * inverse + saturation), 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
            0f, 0f, 0f, 0f, 1f
        };
    }

    private static float[] HueRotateMatrix(double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        return new[]
        {
            (float)(0.213 + cos * 0.787 - sin * 0.213), (float)(0.213 - cos * 0.213 + sin * 0.143), (float)(0.213 - cos * 0.213 - sin * 0.787), 0f, 0f,
            (float)(0.715 - cos * 0.715 - sin * 0.715), (float)(0.715 + cos * 0.285 + sin * 0.140), (float)(0.715 - cos * 0.715 + sin * 0.715), 0f, 0f,
            (float)(0.072 - cos * 0.072 + sin * 0.928), (float)(0.072 - cos * 0.072 - sin * 0.283), (float)(0.072 + cos * 0.928 + sin * 0.072), 0f, 0f,
            0f, 0f, 0f, 1f, 0f,
            0f, 0f, 0f, 0f, 1f
        };
    }

    private static float[] Multiply(float[] left, float[] right)
    {
        var result = new float[25];
        for (var row = 0; row < 5; row++)
        {
            for (var column = 0; column < 5; column++)
            {
                var value = 0f;
                for (var i = 0; i < 5; i++)
                {
                    value += left[row * 5 + i] * right[i * 5 + column];
                }

                result[row * 5 + column] = value;
            }
        }

        return result;
    }

    private static string BuildStatusText(GameFilterSettings settings, bool colorReady)
    {
        if (!settings.IsEnabled && !settings.VignetteEnabled)
        {
            return "滤镜未启用";
        }

        if (settings.IsEnabled && settings.VignetteEnabled)
        {
            return colorReady ? "色彩滤镜和暗角已启用" : "暗角已启用；系统色彩矩阵不可用";
        }

        if (settings.VignetteEnabled)
        {
            return "暗角已启用";
        }

        return colorReady ? "色彩滤镜已启用" : "系统色彩矩阵不可用";
    }

    [DllImport("Magnification.dll", ExactSpelling = true)]
    private static extern bool MagInitialize();

    [DllImport("Magnification.dll", ExactSpelling = true)]
    private static extern bool MagUninitialize();

    [DllImport("Magnification.dll", ExactSpelling = true)]
    private static extern bool MagSetFullscreenColorEffect(ref MagColorEffect effect);

    [StructLayout(LayoutKind.Sequential)]
    private struct MagColorEffect
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 25)]
        public float[] Transform;
    }
}
