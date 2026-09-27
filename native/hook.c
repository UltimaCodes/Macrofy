// Macrofy global WH_KEYBOARD hook DLL.
//
// WH_KEYBOARD_LL (in-process, no DLL) fires BEFORE Raw Input and destroys it when it
// blocks, so per-device blocking is impossible with it. WH_KEYBOARD fires when the
// target app pulls the cooked message - AFTER Raw Input has already identified the
// device - so blocking here keeps the device info intact. The catch is WH_KEYBOARD must
// live in a DLL injected into every process, which is what this file is.
//
// On each key the hook asks the decider window (the Macrofy side, found by window class)
// whether to block, and returns 1 to swallow it. For 32-bit processes Windows can't load
// this 64-bit DLL, so it runs HookProc on Macrofy's own hooking thread instead; the same
// code path works there.
//
// Built without the C runtime (see build.ps1): this DLL is mapped into every process that
// reads keyboard input, so it should carry as little as possible.

#include <windows.h>

// Registered (not a fixed WM_APP number) so no other app's window can ever mistake it
// for one of its own messages. Must match WhKeyboardBackend.HookMessageName.
static const wchar_t HOOK_MESSAGE[]  = L"Macrofy.HookQuery.v1";
static const wchar_t DECIDER_CLASS[] = L"MacrofyDeciderWnd";

static HINSTANCE g_self    = NULL;
static HHOOK     g_hook    = NULL;
static HWND      g_decider = NULL;
static UINT      g_message = 0;

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

// A cached HWND can be recycled for an unrelated window after Macrofy restarts, so check
// the class before trusting it.
static BOOL IsDecider(HWND hwnd)
{
    wchar_t cls[32];
    return hwnd != NULL
        && GetClassNameW(hwnd, cls, sizeof(cls) / sizeof(cls[0])) > 0
        && lstrcmpW(cls, DECIDER_CLASS) == 0;
}

static LRESULT CALLBACK HookProc(int nCode, WPARAM wParam, LPARAM lParam)
{
    if (nCode == HC_ACTION)
    {
        if (g_message == 0)
            g_message = RegisterWindowMessageW(HOOK_MESSAGE);
        if (!IsDecider(g_decider))
            g_decider = FindWindowW(DECIDER_CLASS, NULL);

        if (g_decider != NULL && g_message != 0)
        {
            // wParam = virtual-key code; lParam = the WM_KEY* lParam (bit 31 = released).
            // Synchronous so we get the verdict before returning, but time-bounded and
            // failing open (never block) so a stalled decider can't freeze the keyboard.
            DWORD_PTR verdict = 0;
            if (SendMessageTimeoutW(g_decider, g_message, wParam, lParam,
                                    SMTO_BLOCK | SMTO_ABORTIFHUNG, 80, &verdict) && verdict)
                return 1; // swallow this key
        }
    }
    return CallNextHookEx(NULL, nCode, wParam, lParam);
}

// Bumped whenever the protocol with WhKeyboardBackend changes, so the app can refuse a
// stale MacrofyHook.dll left over from an older download. Must match
// WhKeyboardBackend.ExpectedHookVersion.
__declspec(dllexport) int MacrofyHookVersion(void)
{
    return 2;
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
