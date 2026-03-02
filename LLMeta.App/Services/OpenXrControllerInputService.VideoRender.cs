using LLMeta.App.Models;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.OpenXR;

namespace LLMeta.App.Services;

public sealed unsafe partial class OpenXrControllerInputService
{
    private const int StereoViewCount = 2;
    private const long DxgiFormatB8G8R8A8Unorm = 87;

    private readonly object _videoFrameLock = new();
    private readonly Swapchain[] _colorSwapchains = new Swapchain[StereoViewCount];
    private readonly SwapchainImageD3D11KHR[][] _swapchainImages = new SwapchainImageD3D11KHR[
        StereoViewCount
    ][];
    private readonly ViewConfigurationView[] _viewConfigurationViews = new ViewConfigurationView[
        StereoViewCount
    ];
    private readonly View[] _views = new View[StereoViewCount];
    private readonly byte[][] _eyeScratchBuffers = new byte[StereoViewCount][];
    private readonly int[] _eyeScratchWidths = new int[StereoViewCount];
    private readonly int[] _eyeScratchHeights = new int[StereoViewCount];
    private readonly int[][] _eyeSampleXMaps = new int[StereoViewCount][];
    private readonly int[][] _eyeSampleYMaps = new int[StereoViewCount][];
    private readonly int[] _eyeMapSourceWidths = new int[StereoViewCount];
    private readonly int[] _eyeMapSourceHeights = new int[StereoViewCount];
    private readonly int[] _eyeMapTargetWidths = new int[StereoViewCount];
    private readonly int[] _eyeMapTargetHeights = new int[StereoViewCount];

    private byte[]? _latestSbsBgra;
    private int _latestSbsWidth;
    private int _latestSbsHeight;
    private int _latestSbsVisibleHeight;
    private uint _latestVideoSequence;

    private Result InitializeStereoRendering()
    {
        if (_xr is null)
        {
            return Result.ErrorHandleInvalid;
        }

        uint viewCount = 0;
        var enumerateViewsResult = _xr.EnumerateViewConfigurationView(
            _instance,
            _systemId,
            ViewConfigurationType.PrimaryStereo,
            0,
            ref viewCount,
            (ViewConfigurationView*)0
        );
        if (enumerateViewsResult != Result.Success)
        {
            return enumerateViewsResult;
        }

        if (viewCount != StereoViewCount)
        {
            return Result.ErrorValidationFailure;
        }

        for (var i = 0; i < StereoViewCount; i++)
        {
            _viewConfigurationViews[i] = new ViewConfigurationView
            {
                Type = StructureType.ViewConfigurationView,
            };
            _views[i] = new View { Type = StructureType.View };
        }

        fixed (ViewConfigurationView* viewsPointer = _viewConfigurationViews)
        {
            enumerateViewsResult = _xr.EnumerateViewConfigurationView(
                _instance,
                _systemId,
                ViewConfigurationType.PrimaryStereo,
                viewCount,
                ref viewCount,
                viewsPointer
            );
            if (enumerateViewsResult != Result.Success)
            {
                return enumerateViewsResult;
            }
        }

        uint formatCount = 0;
        var formatResult = _xr.EnumerateSwapchainFormats(_session, 0, ref formatCount, (long*)0);
        if (formatResult != Result.Success)
        {
            return formatResult;
        }

        var formats = new long[formatCount];
        fixed (long* formatsPointer = formats)
        {
            formatResult = _xr.EnumerateSwapchainFormats(
                _session,
                formatCount,
                ref formatCount,
                formatsPointer
            );
            if (formatResult != Result.Success)
            {
                return formatResult;
            }
        }

        var bgraSupported = false;
        foreach (var format in formats)
        {
            if (format == DxgiFormatB8G8R8A8Unorm)
            {
                bgraSupported = true;
                break;
            }
        }

        if (!bgraSupported)
        {
            return Result.ErrorSwapchainFormatUnsupported;
        }

        for (var eye = 0; eye < StereoViewCount; eye++)
        {
            var viewConfig = _viewConfigurationViews[eye];
            var swapchainCreateInfo = new SwapchainCreateInfo
            {
                Type = StructureType.SwapchainCreateInfo,
                CreateFlags = 0,
                UsageFlags =
                    SwapchainUsageFlags.ColorAttachmentBit | SwapchainUsageFlags.SampledBit,
                Format = DxgiFormatB8G8R8A8Unorm,
                SampleCount = viewConfig.RecommendedSwapchainSampleCount,
                Width = viewConfig.RecommendedImageRectWidth,
                Height = viewConfig.RecommendedImageRectHeight,
                FaceCount = 1,
                ArraySize = 1,
                MipCount = 1,
            };
            var createSwapchainResult = _xr.CreateSwapchain(
                _session,
                ref swapchainCreateInfo,
                ref _colorSwapchains[eye]
            );
            if (createSwapchainResult != Result.Success)
            {
                return createSwapchainResult;
            }

            uint imageCount = 0;
            var enumerateImagesResult = _xr.EnumerateSwapchainImages(
                _colorSwapchains[eye],
                0,
                ref imageCount,
                (SwapchainImageBaseHeader*)0
            );
            if (enumerateImagesResult != Result.Success)
            {
                return enumerateImagesResult;
            }

            var images = new SwapchainImageD3D11KHR[imageCount];
            for (var i = 0; i < images.Length; i++)
            {
                images[i].Type = StructureType.SwapchainImageD3D11Khr;
            }

            fixed (SwapchainImageD3D11KHR* imagesPointer = images)
            {
                enumerateImagesResult = _xr.EnumerateSwapchainImages(
                    _colorSwapchains[eye],
                    imageCount,
                    ref imageCount,
                    (SwapchainImageBaseHeader*)imagesPointer
                );
                if (enumerateImagesResult != Result.Success)
                {
                    return enumerateImagesResult;
                }
            }

            _swapchainImages[eye] = images;
        }

        return Result.Success;
    }

    private void DestroyStereoRendering()
    {
        if (_xr is null)
        {
            return;
        }

        for (var eye = 0; eye < StereoViewCount; eye++)
        {
            if (_colorSwapchains[eye].Handle != 0)
            {
                _xr.DestroySwapchain(_colorSwapchains[eye]);
                _colorSwapchains[eye] = default;
            }

            _swapchainImages[eye] = [];
        }
    }

    private Result RenderStereoProjectionLayer(long predictedDisplayTime, out bool rendered)
    {
        rendered = false;
        if (_xr is null || _localSpace.Handle == 0)
        {
            return Result.ErrorHandleInvalid;
        }

        var viewLocateInfo = new ViewLocateInfo
        {
            Type = StructureType.ViewLocateInfo,
            ViewConfigurationType = ViewConfigurationType.PrimaryStereo,
            DisplayTime = predictedDisplayTime,
            Space = _localSpace,
        };
        var viewState = new ViewState { Type = StructureType.ViewState };
        uint viewCountOutput = 0;
        Result locateViewsResult;
        fixed (View* viewsPointer = _views)
        {
            locateViewsResult = _xr.LocateView(
                _session,
                ref viewLocateInfo,
                ref viewState,
                StereoViewCount,
                ref viewCountOutput,
                viewsPointer
            );
        }

        if (locateViewsResult != Result.Success)
        {
            return locateViewsResult;
        }

        if (viewCountOutput < StereoViewCount)
        {
            return Result.ErrorValidationFailure;
        }

        var projectionViews = new CompositionLayerProjectionView[StereoViewCount];

        for (var eye = 0; eye < StereoViewCount; eye++)
        {
            var acquireInfo = new SwapchainImageAcquireInfo
            {
                Type = StructureType.SwapchainImageAcquireInfo,
            };
            uint imageIndex = 0;
            var acquireResult = _xr.AcquireSwapchainImage(
                _colorSwapchains[eye],
                ref acquireInfo,
                ref imageIndex
            );
            if (acquireResult != Result.Success)
            {
                return acquireResult;
            }

            var waitInfo = new SwapchainImageWaitInfo
            {
                Type = StructureType.SwapchainImageWaitInfo,
                Timeout = long.MaxValue,
            };
            var waitResult = _xr.WaitSwapchainImage(_colorSwapchains[eye], ref waitInfo);
            if (waitResult != Result.Success)
            {
                return waitResult;
            }

            var targetTexture = (ID3D11Texture2D*)_swapchainImages[eye][imageIndex].Texture;
            UploadEyeTexture(targetTexture, eye);

            var releaseInfo = new SwapchainImageReleaseInfo
            {
                Type = StructureType.SwapchainImageReleaseInfo,
            };
            var releaseResult = _xr.ReleaseSwapchainImage(_colorSwapchains[eye], ref releaseInfo);
            if (releaseResult != Result.Success)
            {
                return releaseResult;
            }

            projectionViews[eye] = new CompositionLayerProjectionView
            {
                Type = StructureType.CompositionLayerProjectionView,
                Pose = _views[eye].Pose,
                Fov = _views[eye].Fov,
                SubImage = new SwapchainSubImage
                {
                    Swapchain = _colorSwapchains[eye],
                    ImageRect = new Rect2Di
                    {
                        Offset = new Offset2Di { X = 0, Y = 0 },
                        Extent = new Extent2Di
                        {
                            Width = (int)_viewConfigurationViews[eye].RecommendedImageRectWidth,
                            Height = (int)_viewConfigurationViews[eye].RecommendedImageRectHeight,
                        },
                    },
                    ImageArrayIndex = 0,
                },
            };
        }

        fixed (CompositionLayerProjectionView* projectionViewsPointer = projectionViews)
        {
            var projectionLayer = new CompositionLayerProjection
            {
                Type = StructureType.CompositionLayerProjection,
                Space = _localSpace,
                ViewCount = StereoViewCount,
                Views = projectionViewsPointer,
            };
            var layerHeader = (CompositionLayerBaseHeader*)(&projectionLayer);
            CompositionLayerBaseHeader*[] layerHeaders = [layerHeader];
            fixed (CompositionLayerBaseHeader** layerHeadersPointer = layerHeaders)
            {
                var endInfo = new FrameEndInfo
                {
                    Type = StructureType.FrameEndInfo,
                    DisplayTime = predictedDisplayTime,
                    EnvironmentBlendMode = EnvironmentBlendMode.Opaque,
                    LayerCount = 1,
                    Layers = layerHeadersPointer,
                };
                var endResult = _xr.EndFrame(_session, ref endInfo);
                rendered = endResult == Result.Success;
                return endResult;
            }
        }
    }

    private void UpdateLatestSbsFrame(DecodedVideoFrame frame)
    {
        lock (_videoFrameLock)
        {
            _latestSbsBgra = frame.BgraPixels;
            _latestSbsWidth = frame.Width;
            _latestSbsHeight = frame.Height;
            _latestSbsVisibleHeight = ResolveVisibleHeight(frame.Width, frame.Height);
            _latestVideoSequence = frame.Sequence;
        }
    }

    private void UploadEyeTexture(ID3D11Texture2D* texture, int eye)
    {
        if (_d3d11DeviceContext is null)
        {
            return;
        }

        byte[]? latest;
        int sourceWidth;
        int sourceHeight;
        int sourceVisibleHeight;
        lock (_videoFrameLock)
        {
            latest = _latestSbsBgra;
            sourceWidth = _latestSbsWidth;
            sourceHeight = _latestSbsHeight;
            sourceVisibleHeight =
                _latestSbsVisibleHeight > 0 ? _latestSbsVisibleHeight : _latestSbsHeight;
        }

        var targetWidth = (int)_viewConfigurationViews[eye].RecommendedImageRectWidth;
        var targetHeight = (int)_viewConfigurationViews[eye].RecommendedImageRectHeight;
        var eyePixels = EnsureEyeScratchBuffer(eye, targetWidth, targetHeight);
        if (latest is not null && sourceWidth > 1 && sourceHeight > 0 && sourceVisibleHeight > 0)
        {
            CopySbsHalfToTarget(
                latest,
                sourceWidth,
                sourceVisibleHeight,
                eye,
                eyePixels,
                targetWidth,
                targetHeight
            );
        }
        else
        {
            Array.Clear(eyePixels, 0, eyePixels.Length);
        }

        fixed (byte* sourcePointer = eyePixels)
        {
            _d3d11DeviceContext->UpdateSubresource(
                (ID3D11Resource*)texture,
                0,
                (Box*)0,
                sourcePointer,
                (uint)(targetWidth * 4),
                0
            );
        }
    }

    private void CopySbsHalfToTarget(
        byte[] sourceBgra,
        int sourceWidth,
        int sourceVisibleHeight,
        int eye,
        byte[] targetBgra,
        int targetWidth,
        int targetHeight
    )
    {
        var halfWidth = sourceWidth / 2;
        var srcStartX = eye == 0 ? 0 : halfWidth;
        var sourceHeight = sourceVisibleHeight;
        if (halfWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        var (xMap, yMap) = EnsureSamplingMaps(
            eye,
            sourceWidth,
            sourceHeight,
            targetWidth,
            targetHeight
        );
        for (var y = 0; y < targetHeight; y++)
        {
            var srcY = yMap[y];
            var srcRowStart = srcY * sourceWidth * 4;
            var dstRowStart = y * targetWidth * 4;
            for (var x = 0; x < targetWidth; x++)
            {
                var srcX = srcStartX + xMap[x];
                var srcIndex = srcRowStart + (srcX * 4);
                var dstIndex = dstRowStart + (x * 4);
                targetBgra[dstIndex] = sourceBgra[srcIndex];
                targetBgra[dstIndex + 1] = sourceBgra[srcIndex + 1];
                targetBgra[dstIndex + 2] = sourceBgra[srcIndex + 2];
                targetBgra[dstIndex + 3] = 255;
            }
        }
    }

    private (int[] xMap, int[] yMap) EnsureSamplingMaps(
        int eye,
        int sourceWidth,
        int sourceVisibleHeight,
        int targetWidth,
        int targetHeight
    )
    {
        if (
            _eyeSampleXMaps[eye] is not null
            && _eyeSampleYMaps[eye] is not null
            && _eyeMapSourceWidths[eye] == sourceWidth
            && _eyeMapSourceHeights[eye] == sourceVisibleHeight
            && _eyeMapTargetWidths[eye] == targetWidth
            && _eyeMapTargetHeights[eye] == targetHeight
        )
        {
            return (_eyeSampleXMaps[eye]!, _eyeSampleYMaps[eye]!);
        }

        var halfWidth = sourceWidth / 2;
        var xMap = new int[targetWidth];
        for (var x = 0; x < targetWidth; x++)
        {
            xMap[x] = x * halfWidth / targetWidth;
        }

        var yMap = new int[targetHeight];
        for (var y = 0; y < targetHeight; y++)
        {
            yMap[y] = y * sourceVisibleHeight / targetHeight;
        }

        _eyeSampleXMaps[eye] = xMap;
        _eyeSampleYMaps[eye] = yMap;
        _eyeMapSourceWidths[eye] = sourceWidth;
        _eyeMapSourceHeights[eye] = sourceVisibleHeight;
        _eyeMapTargetWidths[eye] = targetWidth;
        _eyeMapTargetHeights[eye] = targetHeight;
        return (xMap, yMap);
    }

    private byte[] EnsureEyeScratchBuffer(int eye, int width, int height)
    {
        var requiredLength = checked(width * height * 4);
        if (
            _eyeScratchBuffers[eye] is null
            || _eyeScratchBuffers[eye].Length != requiredLength
            || _eyeScratchWidths[eye] != width
            || _eyeScratchHeights[eye] != height
        )
        {
            _eyeScratchBuffers[eye] = new byte[requiredLength];
            _eyeScratchWidths[eye] = width;
            _eyeScratchHeights[eye] = height;
        }

        return _eyeScratchBuffers[eye];
    }

    private static int ResolveVisibleHeight(int width, int height)
    {
        if (width == 1920 && height == 1088)
        {
            return 1080;
        }

        return height;
    }
}
