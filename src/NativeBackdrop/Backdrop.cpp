#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <unknwn.h>
#include <dwmapi.h>
#include <d2d1.h>
#include <DispatcherQueue.h>
#include <windows.graphics.interop.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <memory>
#include <vector>
#include <cmath>
#include <cstring>

using namespace winrt;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;

namespace
{
    struct GeometrySource : implements<GeometrySource, Windows::Graphics::IGeometrySource2D, ABI::Windows::Graphics::IGeometrySource2DInterop>
    {
        com_ptr<ID2D1Geometry> geometry;
        explicit GeometrySource(com_ptr<ID2D1Geometry> value) : geometry(std::move(value)) {}
        HRESULT __stdcall GetGeometry(ID2D1Geometry** value) noexcept override
        {
            if (!value) return E_POINTER;
            geometry.copy_to(value); return S_OK;
        }
        HRESULT __stdcall TryGetGeometryUsingFactory(ID2D1Factory*, ID2D1Geometry** value) noexcept override
        {
            if (!value) return E_POINTER;
            *value = nullptr; return S_OK;
        }
    };

    thread_local Windows::System::DispatcherQueueController dispatcher{nullptr};

    LRESULT CALLBACK BackdropProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam)
    {
        if (message == WM_NCHITTEST) return HTTRANSPARENT;
        if (message == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        if (message == WM_ERASEBKGND) return 1;
        return DefWindowProcW(window, message, wparam, lparam);
    }

    struct Backdrop
    {
        HWND window{};
        HWND foreground{};
        Compositor compositor{nullptr};
        DesktopWindowTarget target{nullptr};
        SpriteVisual visual{nullptr};
        com_ptr<ID2D1Factory> factory;
        std::vector<D2D1_POINT_2F> lastOutline;

        explicit Backdrop(HWND content) : foreground(content)
        {
            if (!Windows::System::DispatcherQueue::GetForCurrentThread())
            {
                DispatcherQueueOptions options{sizeof(options), DQTYPE_THREAD_CURRENT, DQTAT_COM_NONE};
                check_hresult(CreateDispatcherQueueController(options,
                    reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(dispatcher))));
            }
            WNDCLASSW cls{};
            cls.lpfnWndProc = BackdropProc;
            cls.hInstance = GetModuleHandleW(nullptr);
            cls.lpszClassName = L"HUDI.ShapedMatte";
            if (!RegisterClassW(&cls) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) throw_last_error();
            window = CreateWindowExW(WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT,
                cls.lpszClassName, L"HUDI — матовый фон", WS_POPUP, 0, 0, 1, 1,
                GetWindow(content, GW_OWNER), nullptr, cls.hInstance, nullptr);
            if (!window) throw_last_error();
            try
            {
                BOOL enabled = TRUE;
                check_hresult(DwmSetWindowAttribute(window, DWMWA_USE_HOSTBACKDROPBRUSH, &enabled, sizeof(enabled)));
                int noRounding = DWMWCP_DONOTROUND;
                DwmSetWindowAttribute(window, DWMWA_WINDOW_CORNER_PREFERENCE, &noRounding, sizeof(noRounding));
                compositor = Compositor();
                check_hresult(compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>()->CreateDesktopWindowTarget(
                    window, false, reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
                visual = compositor.CreateSpriteVisual();
                visual.Brush(compositor.CreateHostBackdropBrush());
                target.Root(visual);
                check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.put()));
            }
            catch (...) { DestroyWindow(window); window = nullptr; throw; }
        }

        ~Backdrop()
        {
            try { if (target) { target.Root(nullptr); target.Close(); } } catch (...) {}
            if (window) DestroyWindow(window);
        }

        void Update(D2D1_POINT_2F const* points, int count, int x, int y, int width, int height, float opacity, bool visible)
        {
            if (!visible || width < 1 || height < 1) { ShowWindow(window, SW_HIDE); return; }
            if (!points || count < 3) throw_hresult(E_INVALIDARG);
            if (lastOutline.size() != static_cast<size_t>(count) || memcmp(lastOutline.data(), points, count * sizeof(*points)) != 0)
            {
                com_ptr<ID2D1PathGeometry> path;
                check_hresult(factory->CreatePathGeometry(path.put()));
                com_ptr<ID2D1GeometrySink> sink;
                check_hresult(path->Open(sink.put()));
                sink->SetFillMode(D2D1_FILL_MODE_WINDING);
                sink->BeginFigure(points[0], D2D1_FIGURE_BEGIN_FILLED);
                sink->AddLines(points + 1, count - 1);
                sink->EndFigure(D2D1_FIGURE_END_CLOSED);
                check_hresult(sink->Close());
                auto geometry = compositor.CreatePathGeometry(CompositionPath(make<GeometrySource>(path.as<ID2D1Geometry>())));
                visual.Clip(compositor.CreateGeometricClip(geometry));
                // The compositor owns the smooth visible edge. A one-pixel
                // expanded HRGN only excludes empty corners from HWND hit tests.
                HRGN region{};
                for (int dy=-1; dy<=1; ++dy) for (int dx=-1; dx<=1; ++dx)
                {
                    std::vector<POINT> hitPoints;
                    hitPoints.reserve(count);
                    for (int i=0; i<count; ++i) hitPoints.push_back({static_cast<LONG>(std::lround(points[i].x))+dx, static_cast<LONG>(std::lround(points[i].y))+dy});
                    auto part=CreatePolygonRgn(hitPoints.data(),count,WINDING);
                    if (!part) { if(region)DeleteObject(region); throw_last_error(); }
                    if (!region) region=part;
                    else { CombineRgn(region,region,part,RGN_OR);DeleteObject(part); }
                }
                if (!SetWindowRgn(window,region,FALSE)) {DeleteObject(region);throw_last_error();}
                lastOutline.assign(points, points + count);
            }
            visual.Size({static_cast<float>(width), static_cast<float>(height)});
            visual.Opacity(opacity);
            // Sibling of the content window, immediately behind it. Owning it
            // by the content itself would force it above the crisp WPF text.
            SetWindowLongPtrW(window, GWLP_HWNDPARENT, reinterpret_cast<LONG_PTR>(GetWindow(foreground, GW_OWNER)));
            if (!SetWindowPos(window, foreground, x, y, width, height, SWP_NOACTIVATE | SWP_SHOWWINDOW)) throw_last_error();
        }
    };
}

extern "C" __declspec(dllexport) HRESULT __cdecl HudiBackdropCreate(HWND content, void** result) noexcept
{
    if (!result) return E_POINTER;
    *result = nullptr;
    try { *result = new Backdrop(content); return S_OK; }
    catch (...) { return to_hresult(); }
}

extern "C" __declspec(dllexport) HRESULT __cdecl HudiBackdropUpdate(void* state, D2D1_POINT_2F const* points, int count,
    int x, int y, int width, int height, float opacity, BOOL visible) noexcept
{
    if (!state) return E_POINTER;
    try { static_cast<Backdrop*>(state)->Update(points, count, x, y, width, height, opacity, visible != FALSE); return S_OK; }
    catch (...) { return to_hresult(); }
}

extern "C" __declspec(dllexport) void __cdecl HudiBackdropDestroy(void* state) noexcept
{
    try { delete static_cast<Backdrop*>(state); } catch (...) {}
}

extern "C" __declspec(dllexport) HWND __cdecl HudiBackdropWindow(void* state) noexcept
{
    return state ? static_cast<Backdrop*>(state)->window : nullptr;
}
