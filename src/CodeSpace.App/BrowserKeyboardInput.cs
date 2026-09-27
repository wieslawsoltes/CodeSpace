using CodeSpace.Controls.Uno;
using Windows.System;

namespace CodeSpace.App;

internal static class BrowserKeyboardInput
{
    public static void Initialize()
    {
#if __WASM__
        // Capture physical DOM modifier state before Uno routes events from native and
        // accessibility text inputs. Ignoring synthetic replays avoids a stuck modifier
        // after a focus transfer, while blur/visibility changes clear abandoned keys.
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("""
            (() => {
                if (globalThis.CodeSpaceKeys) return 'ready';
                const state = globalThis.CodeSpaceKeys = { control: false, shift: false, alt: false, meta: false };
                const update = event => {
                    if (!event.isTrusted) return;
                    state.control = !!event.ctrlKey;
                    state.shift = !!event.shiftKey;
                    state.alt = !!event.altKey;
                    state.meta = !!event.metaKey;
                };
                const reset = () => { state.control = state.shift = state.alt = state.meta = false; };
                window.addEventListener('keydown', update, true);
                window.addEventListener('keyup', update, true);
                window.addEventListener('pointerdown', update, true);
                window.addEventListener('pointermove', update, true);
                window.addEventListener('wheel', update, { capture: true, passive: true });
                window.addEventListener('blur', reset);
                document.addEventListener('visibilitychange', () => { if (document.hidden) reset(); });
                return 'ready';
            })()
            """);
        KeyModifiers.KeyStateOverride = key =>
        {
            var name = key switch
            {
                VirtualKey.Control => "control", VirtualKey.Shift => "shift", VirtualKey.Menu => "alt",
                VirtualKey.LeftWindows or VirtualKey.RightWindows => "meta", _ => null
            };
            return name is null ? null : Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.CodeSpaceKeys." + name + " ? 'down' : 'up'") == "down";
        };
#endif
    }
}
