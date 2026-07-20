// Input adapter for macOS
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2024-2026 OpenRCT3 Contributors. All rights reserved.

using System;
using System.Collections.Generic;
using System.Numerics;
using AppKit;
using Foundation;
using Silk.NET.Input;
using OpenRCT3.Platforms.Input;
using OpenCobra.GDK.Platform;

namespace OpenRCT3.Platforms.macOS {
  // Minimal mouse implementation used by the GUI controller
  internal class MacMouse : IMouse {
    public event Action<IMouse, MouseButton>? MouseDown;
    public event Action<IMouse, MouseButton>? MouseUp;
    public event Action<IMouse, MouseButton, Vector2>? Click;
    public event Action<IMouse, MouseButton, Vector2>? DoubleClick;
    public event Action<IMouse, Vector2>? MouseMove;
    public event Action<IMouse, ScrollWheel>? Scroll;

    // IInputDevice members
    public string Name => "macOS Mouse";
    public int Index => 0;
    public bool IsConnected => true;

    public IReadOnlyList<MouseButton> SupportedButtons => new[] { MouseButton.Left, MouseButton.Middle, MouseButton.Right };
    public IReadOnlyList<ScrollWheel> ScrollWheels => new[] { new ScrollWheel(0,0) };

    public Vector2 Position { get; set; }
    public ICursor Cursor => throw new NotImplementedException();
    public int DoubleClickTime { get => 500; set {} }
    public int DoubleClickRange { get => 4; set {} }

    private readonly HashSet<MouseButton> pressed = new();

    public void BeginInput() { }
    public void EndInput() { }
    public bool IsButtonPressed(MouseButton btn) => pressed.Contains(btn);

    // Helpers called by the adapter
    public void OnMouseDown(MouseButton btn, Vector2 pos) {
      pressed.Add(btn);
      Position = pos;
      MouseDown?.Invoke(this, btn);
    }
    public void OnMouseUp(MouseButton btn, Vector2 pos) {
      pressed.Remove(btn);
      Position = pos;
      MouseUp?.Invoke(this, btn);
      Click?.Invoke(this, btn, pos);
    }
    public void OnMouseMove(Vector2 pos) {
      Position = pos;
      MouseMove?.Invoke(this, pos);
    }
    public void OnScroll(float dx, float dy) {
      Scroll?.Invoke(this, new ScrollWheel(dx, dy));
    }
  }

  // Minimal keyboard implementation used by the GUI controller
  internal class MacKeyboard : IKeyboard {
    private readonly HashSet<Key> pressed = new();
    private readonly HashSet<int> pressedScancodes = new();

    public event Action<IKeyboard, Key, int>? KeyDown;
    public event Action<IKeyboard, Key, int>? KeyUp;
    public event Action<IKeyboard, char>? KeyChar;

    // IInputDevice members
    public string Name => "macOS Keyboard";
    public int Index => 0;
    public bool IsConnected => true;

    public IReadOnlyList<Key> SupportedKeys => new[] {
      Key.W,
      Key.A,
      Key.S,
      Key.D,
      Key.Q,
      Key.E,
      Key.Up,
      Key.Down,
      Key.Left,
      Key.Right,
    };
    public string ClipboardText { get => NSPasteboard.GeneralPasteboard.GetStringForType(NSPasteboard.NSPasteboardTypeString) ?? string.Empty; set { var pb = NSPasteboard.GeneralPasteboard; pb.ClearContents(); pb.SetStringForType(value, NSPasteboard.NSPasteboardTypeString); } }

    public void BeginInput() { }
    public void EndInput() { }
    public bool IsKeyPressed(Key key) => pressed.Contains(key);
    public bool IsScancodePressed(int scancode) => pressedScancodes.Contains(scancode);

    // Helpers
    public void OnKeyDown(Key key, int scancode) {
      pressed.Add(key);
      pressedScancodes.Add(scancode);
      KeyDown?.Invoke(this, key, scancode);
    }
    public void OnKeyUp(Key key, int scancode) {
      pressed.Remove(key);
      pressedScancodes.Remove(scancode);
      KeyUp?.Invoke(this, key, scancode);
    }
    public void OnKeyChar(char ch) => KeyChar?.Invoke(this, ch);
  }

  // Mac input context using NSEvent local monitors. Keeps lifetime minimal and removes monitors on Dispose.
  internal class MacInputContext : InputContext {
    private NSObject? mouseMonitor;
    private NSObject? keyMonitor;
    private readonly MacMouse mouse;
    private readonly MacKeyboard keyboard;
    private readonly NSView gameView;

    public MacInputContext(nint ownerWindowHandle) : this(ResolveGameView(ownerWindowHandle)) { }

    public MacInputContext(NSView gameView) : base((nint)gameView.Handle) {
      this.gameView = gameView;
      mouse = new MacMouse();
      keyboard = new MacKeyboard();
      mice.Add(mouse);
      keyboards.Add(keyboard);

      // Combine mouse-related event types into a mask
      var mouseMask = NSEventMask.MouseMoved | NSEventMask.LeftMouseDragged |
                      NSEventMask.RightMouseDragged | NSEventMask.OtherMouseDragged |
                      NSEventMask.LeftMouseDown | NSEventMask.LeftMouseUp |
                      NSEventMask.RightMouseDown | NSEventMask.RightMouseUp |
                      NSEventMask.OtherMouseDown | NSEventMask.OtherMouseUp |
                      NSEventMask.ScrollWheel;

      mouseMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
        mouseMask,
        MonitorMouseEvent);

      var keyMask = NSEventMask.KeyDown | NSEventMask.KeyUp;
      keyMonitor = NSEvent.AddLocalMonitorForEventsMatchingMask(
        keyMask,
        MonitorKeyEvent);
    }

    private NSEvent MonitorMouseEvent(NSEvent ev) {
      if (OwnsMouseEvent(ev)) HandleMouseEvent(ev);
      return ev;
    }

    private NSEvent MonitorKeyEvent(NSEvent ev) {
      if (OwnsKeyEvent(ev)) HandleKeyEvent(ev);
      return ev;
    }

    private bool OwnsMouseEvent(NSEvent ev) {
      var eventWindow = ev.Window;
      var ownerWindowHandle = GetWindowHandle(gameView.Window);
      var eventWindowHandle = GetWindowHandle(eventWindow);
      if (!IsOwnedWindow(ownerWindowHandle, eventWindowHandle)) return false;

      var location = gameView.ConvertPointFromView(ev.LocationInWindow, null);
      return IsOwnedViewTarget(
        ownerWindowHandle,
        eventWindowHandle,
        gameView.HitTest(location) != null);
    }

    private bool OwnsKeyEvent(NSEvent ev) {
      var eventWindow = ev.Window;
      var ownerWindowHandle = GetWindowHandle(gameView.Window);
      var eventWindowHandle = GetWindowHandle(eventWindow);
      var ownsResponder = eventWindow?.FirstResponder is NSView responderView &&
        IsViewInAncestry((nint)gameView.Handle, GetViewAncestry(responderView));
      return IsOwnedViewTarget(ownerWindowHandle, eventWindowHandle, ownsResponder);
    }

    internal static bool IsOwnedWindow(nint ownerWindow, nint eventWindow) =>
      ownerWindow != nint.Zero && eventWindow == ownerWindow;

    internal static bool IsOwnedViewTarget(
      nint ownerWindow,
      nint eventWindow,
      bool targetsGameView) =>
      targetsGameView && IsOwnedWindow(ownerWindow, eventWindow);

    internal static bool IsViewInAncestry(nint gameView, IEnumerable<nint> ancestry) {
      if (gameView == nint.Zero) return false;
      foreach (var view in ancestry)
        if (view == gameView) return true;
      return false;
    }

    private static IEnumerable<nint> GetViewAncestry(NSView view) {
      for (var current = (NSView?)view; current != null; current = current.Superview)
        yield return (nint)current.Handle;
    }

    private static nint GetWindowHandle(NSWindow? window) => window == null
      ? nint.Zero
      : (nint)window.Handle;

    private static NSView ResolveGameView(nint ownerWindowHandle) {
      foreach (var window in NSApplication.SharedApplication.Windows) {
        if ((nint)window.Handle != ownerWindowHandle) continue;
        if (window.ContentViewController is GameViewController controller)
          return controller.Game;
      }
      throw new InvalidOperationException("The input owner window has no game view.");
    }

    private void HandleMouseEvent(NSEvent ev) {
      // AppKit window coordinates are bottom-up, while Silk/WinForms mouse coordinates are top-down.
      // Normalize here so shared gestures (especially vertical camera orbit) behave identically.
      var loc = ev.LocationInWindow;
      var window = ev.Window
        ?? throw new InvalidOperationException("A monitored mouse event has no owning window.");
      var height = window.ContentView?.Bounds.Height ?? window.Frame.Height;
      var pos = ToTopLeftPosition(
        new Vector2((float)loc.X, (float)loc.Y),
        (float)height);
      switch (ev.Type) {
        case NSEventType.LeftMouseDown:
          mouse.OnMouseDown(MouseButton.Left, pos);
          break;
        case NSEventType.LeftMouseUp:
          mouse.OnMouseUp(MouseButton.Left, pos);
          break;
        case NSEventType.RightMouseDown:
          mouse.OnMouseDown(MouseButton.Right, pos);
          break;
        case NSEventType.RightMouseUp:
          mouse.OnMouseUp(MouseButton.Right, pos);
          break;
        case NSEventType.OtherMouseDown:
          mouse.OnMouseDown(MouseButton.Middle, pos);
          break;
        case NSEventType.OtherMouseUp:
          mouse.OnMouseUp(MouseButton.Middle, pos);
          break;
        case NSEventType.MouseMoved:
        case NSEventType.LeftMouseDragged:
        case NSEventType.RightMouseDragged:
        case NSEventType.OtherMouseDragged:
          mouse.OnMouseMove(pos);
          break;
        case NSEventType.ScrollWheel:
          mouse.OnScroll((float)ev.DeltaX, (float)ev.DeltaY);
          break;
        default:
          break;
      }
    }

    internal static Vector2 ToTopLeftPosition(Vector2 position, float height) =>
      new(position.X, height - position.Y);

    private void HandleKeyEvent(NSEvent ev) {
      switch (ev.Type) {
        case NSEventType.KeyDown:
          // Map character and keycode
          if (!string.IsNullOrEmpty(ev.CharactersIgnoringModifiers)) {
            foreach (var ch in ev.CharactersIgnoringModifiers) keyboard.OnKeyChar(ch);
          }
          // Fire KeyDown with the platform-independent key and native scancode.
          keyboard.OnKeyDown(ToSilkKey(ev.KeyCode), (int)ev.KeyCode);
          break;
        case NSEventType.KeyUp:
          keyboard.OnKeyUp(ToSilkKey(ev.KeyCode), (int)ev.KeyCode);
          break;
      }
    }

    private static Key ToSilkKey(ushort keyCode) => keyCode switch {
      0 => Key.A,
      1 => Key.S,
      2 => Key.D,
      12 => Key.Q,
      13 => Key.W,
      14 => Key.E,
      123 => Key.Left,
      124 => Key.Right,
      125 => Key.Down,
      126 => Key.Up,
      _ => Key.Unknown,
    };

    public override void Dispose() {
      if (mouseMonitor != null) {
        NSEvent.RemoveMonitor(mouseMonitor);
        mouseMonitor = null;
      }
      if (keyMonitor != null) {
        NSEvent.RemoveMonitor(keyMonitor);
        keyMonitor = null;
      }
      base.Dispose();
    }
  }
}
