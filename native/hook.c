// Macrofy global WH_KEYBOARD hook DLL.
//
// WH_KEYBOARD_LL (in-process, no DLL) fires BEFORE Raw Input and destroys it when it
// blocks, so per-device blocking is impossible with it. WH_KEYBOARD fires when the
// target app pulls the cooked message — AFTER Raw Input has already identified the
// device — so blocking here keeps the device info intact. The catch is WH_KEYBOARD must
// live in a DLL injected into every process, which is what this file is.
//
// On each key the hook asks the decider window (the Macrofy side, found by window class)
// whether to block, and returns 1 to swallow it. Build (MinGW): see build.ps1.

#include <windows.h>

#define WM_HOOK 0x8101
static const wchar_t DECIDER_CLASS[] = L"MacrofyDeciderWnd";

static HINSTANCE g_self    = NULL;
static HHOOK     g_hook    = NULL;
static HWND      g_decider = NULL;

BOOL WINAPI DllMain(HINSTANCE hinst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = hinst;
        DisableThreadLibraryCalls(hinst);
    }
    return TRUE;
}

static LRESULT CALLBACK HookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode == HC_ACTION)
    {
        if (g_decider == NULL || !IsWindow(g_decider))
            g_decider = FindWindowW(DECIDER_CLASS, NULL);

        if (g_decider)
        {
            // wParam = virtual-key code; lParam = the WM_KEY* lParam (bit 31 = released).
            // Synchronous so we get the verdict before returning, but time-bounded and
            // failing open (never block) so a stalled decider can't freeze the keyboard.
            DWORD_PTR verdict = 0;
            if (SendMessageTimeoutW(g_decider, WM_HOOK, wParam, lParam,
                                    SMTO_BLOCK | SMTO_ABORTIFHUNG, 80, &verdict) && verdict)
                return 1; // swallow this key
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

__declspec(dllexport) BOOL StartHook(void)
{
    if (g_hook) return TRUE;
    g_hook = SetWindowsHookExW(WH_KEYBOARD, HookProc, g_self, 0);
    return g_hook != NULL;
}

__declspec(dllexport) BOOL StopHook(void)
{
    if (g_hook) { UnhookWindowsHookEx(g_hook); g_hook = NULL; }
    return TRUE;
}
