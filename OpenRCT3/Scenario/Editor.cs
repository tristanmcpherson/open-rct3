// Scenario Editor
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using Hexa.NET.ImGui;
using OpenCobra.GDK.GUI;
using System.Numerics;

namespace OpenRCT3.Scenario;

public class Editor : IWindow {
  // Wide enough to fit the "Choose Objectives & Challenges" button's label.
  private const float ButtonWidth = 235;

  public bool Open { get; private set; } = true;

  public void Render() {
    if (!Open) return;

    var open = Open;
    ImGui.SetNextWindowSize(new Vector2(ButtonWidth + ImGui.GetStyle().WindowPadding.X * 2, 0), ImGuiCond.Once);
    ImGui.Begin("Scenario Editor", ref open, ImGuiWindowFlags.NoResize);

    ImGui.TextDisabled("Read-only preview");
    ImGui.TextWrapped("Scenario editing is not available yet.");

    // Row of available panel actions
    // TODO: Save scenario
    ImGui.BeginDisabled(true);
    ImGui.Button("Save");
    ImGui.EndDisabled();
    ImGui.SameLine();
    if (ImGui.Button("Close panel")) open = false;
    // TODO: Quit the game through a platform-owned window shutdown request.

    ImGui.Separator();

    // Column of labeled buttons
    ImGui.BeginDisabled(true);
    // TODO: Open park dialog
    ImGui.Button("Setup Park", new Vector2(ButtonWidth, 0));
    // TODO: Open finances dialog
    ImGui.Button("Choose Finances", new Vector2(ButtonWidth, 0));
    // TODO: Open objectives dialog
    ImGui.Button("Choose Objectives & Challenges", new Vector2(ButtonWidth, 0));
    ImGui.EndDisabled();

    ImGui.Separator();
    ImGui.TextDisabled("Camera controls");
    ImGui.TextWrapped("WASD or arrows: pan\nQ/E or right-drag: orbit\nMouse wheel: zoom");

    ImGui.End();
    if (open != Open) Open = open;
  }
}
