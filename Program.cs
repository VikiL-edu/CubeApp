// VoxelDemo.cs
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.Json;
using System.Runtime.Serialization;
using System.Drawing.Printing;

public enum GameState
{
    MainMenu,
    Playing,
    Exiting
}

public class MainMenu
{
    public enum MenuState { Main, CreateWorld, DeleteWorld, SelectWorld, ConfirmDelete, Settings }
    public MenuState State = MenuState.Main;
    public int SelectedIndex = 0;
    public List<string> WorldList = new();
    public string NewWorldName = "";
    public int SelectedWorldIndex = 0;
    public bool StartGameSelected = false;
    public bool ExitSelected = false;
    public bool DeleteConfirmed = false;
    public string DeleteCandidate = "";
    public Settings MenuSettings = new Settings();
    private int SettingsSelectedIndex = 0;
    // Add a field for transient error/info messages
    public string MenuMessage = "";
    private double _messageTimer = 0;
    private const double MessageDuration = 2.5; // seconds

    // --- Text rendering cache ---
    private class TextCacheEntry
    {
        public int TextureId;
        public int Width;
        public int Height;
        public float Scale;
        public string? Text; // nullable to satisfy compiler
        public System.Drawing.Color Color;
        public int LastFrameUsed;
    }
    private Dictionary<(string, float, System.Drawing.Color), TextCacheEntry> _textCache = new();
    private int _frameCounter = 0;
    private const int TextCacheMaxAge = 60; // frames

    // --- Input debounce ---
    private Dictionary<Keys, bool> _keyWasDown = new();

    public MainMenu()
    {
        RefreshWorldList();
        MenuSettings.Load();
    }

    public void RefreshWorldList()
    {
        WorldList.Clear();
        string saveRoot = Path.Combine("save");
        if (Directory.Exists(saveRoot))
        {
            foreach (var dir in Directory.GetDirectories(saveRoot))
            {
                string name = Path.GetFileName(dir);
                if (!string.IsNullOrWhiteSpace(name))
                    WorldList.Add(name);
            }
        }
    }

    public void Update(KeyboardState input, MouseState mouse, int windowWidth, int windowHeight)
    {
        _frameCounter++;
        // Helper for debounced key press
        bool DebouncedKey(Keys key)
        {
            bool isDown = input.IsKeyDown(key);
            bool wasDown = _keyWasDown.TryGetValue(key, out var prev) ? prev : false;
            _keyWasDown[key] = isDown;
            return isDown && !wasDown;
        }
        // Mouse support
        bool mouseClicked = mouse.IsButtonDown(MouseButton.Left);
        static bool PointInRect(float px, float py, float x, float y, float w, float h) => px >= x && px <= x + w && py >= y && py <= y + h;
        float leftX = -0.98f;
        float y = 0.75f;
        float scaleTitle = 1.6f;
        float scaleOption = 1.3f;
        float scaleInfo = 1.1f;
        float scaleSub = 1.2f;
        float GetTextHeight(string text, float scale)
        {
            using (var bmp = new System.Drawing.Bitmap(512, 64))
            using (var gfx = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 32, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
            {
                var textSize = gfx.MeasureString(text, font);
                return (float)textSize.Height / windowHeight * 2.0f * scale;
            }
        }
        // Convert mouse position to NDC
        float mouseNdcX = (mouse.X / (float)windowWidth) * 2.0f - 1.0f;
        float mouseNdcY = 0.90f - (mouse.Y / (float)windowHeight) * 2.0f;
        if (State == MenuState.Main)
        {
            string[] options = { "Play World", "Create New World", "Delete World", "Settings", "Exit" };
            float optionY = y - GetTextHeight("==== VOXEL GAME ====", scaleTitle) * 1.1f;
            int hovered = -1;
            // Precompute all option Y positions for hit testing, matching render spacing
            List<(float top, float bot)> optionBounds = new();
            for (int i = 0; i < options.Length; i++)
            {
                string optText = (i == SelectedIndex ? "> " : "  ") + options[i];
                float optH = GetTextHeight(optText, scaleOption);
                float optTop = optionY;
                float optBot = optionY - optH;
                optionBounds.Add((optTop, optBot));
                optionY -= optH * 1.2f; // match render spacing
            }
            // Now test mouse against bounds
            float optLeft = leftX + 0.04f;
            float optRight = optLeft + 0.7f;
            for (int i = 0; i < optionBounds.Count; i++)
            {
                if (mouseNdcX >= optLeft && mouseNdcX <= optRight && mouseNdcY <= optionBounds[i].top && mouseNdcY >= optionBounds[i].bot)
                {
                    hovered = i;
                }
            }
            if (hovered != -1)
            {
                SelectedIndex = hovered;
                if (mouseClicked)
                {
                    if (SelectedIndex == 0) { State = MenuState.SelectWorld; SelectedWorldIndex = 0; }
                    if (SelectedIndex == 1) { State = MenuState.CreateWorld; NewWorldName = ""; }
                    if (SelectedIndex == 2) { State = MenuState.DeleteWorld; SelectedWorldIndex = 0; }
                    if (SelectedIndex == 3) { State = MenuState.Settings; }
                    if (SelectedIndex == 4) { ExitSelected = true; }
                }
            }
            if (DebouncedKey(Keys.Up))
                SelectedIndex = (SelectedIndex - 1 + options.Length) % options.Length;
            if (DebouncedKey(Keys.Down))
                SelectedIndex = (SelectedIndex + 1) % options.Length;
            if (DebouncedKey(Keys.Enter))
            {
                if (SelectedIndex == 0) { State = MenuState.SelectWorld; SelectedWorldIndex = 0; }
                if (SelectedIndex == 1) { State = MenuState.CreateWorld; NewWorldName = ""; }
                if (SelectedIndex == 2) { State = MenuState.DeleteWorld; SelectedWorldIndex = 0; }
                if (SelectedIndex == 3) { State = MenuState.Settings; }
                if (SelectedIndex == 4) { ExitSelected = true; }
            }
        }
        else if (State == MenuState.CreateWorld)
        {
            if (DebouncedKey(Keys.Backspace) && NewWorldName.Length > 0)
                NewWorldName = NewWorldName.Substring(0, NewWorldName.Length - 1);

            // Only allow A-Z, 0-9 input (no enum loop)
            for (Keys key = Keys.A; key <= Keys.Z; key++)
            {
                if (DebouncedKey(key))
                {
                    char c = (char)('A' + (key - Keys.A));
                    if (NewWorldName.Length < 16)
                        NewWorldName += c;
                }
            }
            for (Keys key = Keys.D0; key <= Keys.D9; key++)
            {
                if (DebouncedKey(key))
                {
                    char c = (char)('0' + (key - Keys.D0));
                    if (NewWorldName.Length < 16)
                        NewWorldName += c;
                }
            }
            // Allow space character in world name
            if (DebouncedKey(Keys.Space) && NewWorldName.Length < 16)
            {
                NewWorldName += ' ';
            }

            if (DebouncedKey(Keys.Enter))
            {
                string trimmedName = NewWorldName.Trim();
                bool nameExists = WorldList.Contains(trimmedName, StringComparer.OrdinalIgnoreCase);
                if (string.IsNullOrWhiteSpace(trimmedName))
                {
                    MenuMessage = "World name cannot be empty!";
                    _messageTimer = MessageDuration;
                }
                else if (nameExists)
                {
                    MenuMessage = $"World '{trimmedName}' already exists!";
                    _messageTimer = MessageDuration;
                }
                else
                {
                    string newDir = Path.Combine("save", trimmedName);
                    Directory.CreateDirectory(Path.Combine(newDir, "chunks"));
                    RefreshWorldList();
                    VoxelWindow.CurrentWorldName = trimmedName;
                    StartGameSelected = true;
                    State = MenuState.Main;
                    MenuMessage = "World created!";
                    _messageTimer = MessageDuration;
                }
            }
            if (DebouncedKey(Keys.Escape)) State = MenuState.Main;
        }
        else if (State == MenuState.SelectWorld)
        {
            if (WorldList.Count == 0) { State = MenuState.Main; return; }
            float optionY = y - GetTextHeight("Select world to play:", scaleOption) * 1.1f;
            int hovered = -1;
            for (int i = 0; i < WorldList.Count; i++)
            {
                string optText = (i == SelectedWorldIndex ? "> " : "  ") + WorldList[i];
                float optH = GetTextHeight(optText, scaleSub) * 1.1f;
                float optTop = optionY;
                float optBot = optionY - optH;
                float optLeft = leftX + 0.04f;
                float optRight = optLeft + 0.7f;
                if (mouseNdcX >= optLeft && mouseNdcX <= optRight && mouseNdcY >= optBot && mouseNdcY <= optTop)
                {
                    hovered = i;
                }
                optionY -= optH;
            }
            if (hovered != -1)
            {
                SelectedWorldIndex = hovered;
                if (mouseClicked)
                {
                    VoxelWindow.CurrentWorldName = WorldList[SelectedWorldIndex];
                    StartGameSelected = true;
                }
            }
            if (DebouncedKey(Keys.Up))
                SelectedWorldIndex = (SelectedWorldIndex - 1 + WorldList.Count) % WorldList.Count;
            if (DebouncedKey(Keys.Down))
                SelectedWorldIndex = (SelectedWorldIndex + 1) % WorldList.Count;
            if (DebouncedKey(Keys.Enter))
            {
                VoxelWindow.CurrentWorldName = WorldList[SelectedWorldIndex];
                StartGameSelected = true;
            }
            if (DebouncedKey(Keys.Escape)) State = MenuState.Main;
        }
        else if (State == MenuState.DeleteWorld)
        {
            if (WorldList.Count == 0) { State = MenuState.Main; return; }
            float optionY = y - GetTextHeight("Select world to delete:", scaleOption) * 1.1f;
            int hovered = -1;
            for (int i = 0; i < WorldList.Count; i++)
            {
                string optText = (i == SelectedWorldIndex ? "> " : "  ") + WorldList[i];
                float optH = GetTextHeight(optText, scaleSub) * 1.1f;
                float optTop = optionY;
                float optBot = optionY - optH;
                float optLeft = leftX + 0.04f;
                float optRight = optLeft + 0.7f;
                if (mouseNdcX >= optLeft && mouseNdcX <= optRight && mouseNdcY >= optBot && mouseNdcY <= optTop)
                {
                    hovered = i;
                }
                optionY -= optH;
            }
            if (hovered != -1)
            {
                SelectedWorldIndex = hovered;
                if (mouseClicked)
                {
                    DeleteCandidate = WorldList[SelectedWorldIndex];
                    State = MenuState.ConfirmDelete;
                }
            }
            if (DebouncedKey(Keys.Up))
                SelectedWorldIndex = (SelectedWorldIndex - 1 + WorldList.Count) % WorldList.Count;
            if (DebouncedKey(Keys.Down))
                SelectedWorldIndex = (SelectedWorldIndex + 1) % WorldList.Count;
            if (DebouncedKey(Keys.Enter))
            {
                DeleteCandidate = WorldList[SelectedWorldIndex];
                State = MenuState.ConfirmDelete;
            }
            if (DebouncedKey(Keys.Escape)) State = MenuState.Main;
        }
        else if (State == MenuState.ConfirmDelete)
        {
            if (DebouncedKey(Keys.Y))
            {
                string delDir = Path.Combine("save", DeleteCandidate);
                if (Directory.Exists(delDir))
                    Directory.Delete(delDir, true);
                RefreshWorldList();
                State = MenuState.Main;
            }
            if (DebouncedKey(Keys.N) || DebouncedKey(Keys.Escape))
                State = MenuState.Main;
        }
        else if (State == MenuState.Settings)
        {
            // Only one setting for now: Render Distance
            if (DebouncedKey(Keys.Left))
            {
                if (MenuSettings.RenderDistance > 1) MenuSettings.RenderDistance--;
            }
            if (DebouncedKey(Keys.Right))
            {
                if (MenuSettings.RenderDistance < 16) MenuSettings.RenderDistance++;
            }
            // Vertical Render Distance controls
            if (MenuSettings.VerticalRenderDistance > 1 && input.IsKeyDown(Keys.Up)) MenuSettings.VerticalRenderDistance--;
            if (MenuSettings.VerticalRenderDistance < 16 && input.IsKeyDown(Keys.Down)) MenuSettings.VerticalRenderDistance++;
            if (DebouncedKey(Keys.Enter))
            {
                MenuSettings.Save();
                MenuSettings.Load();
                State = MenuState.Main;
                MenuMessage = "Settings saved.";
                _messageTimer = MessageDuration;
            }
            if (DebouncedKey(Keys.Escape)) State = MenuState.Main;
        }
        // Update message timer
        if (_messageTimer > 0)
        {
            _messageTimer -= 1.0 / 60.0; // Assume 60 FPS for timer 
            if (_messageTimer <= 0) MenuMessage = "";
        }
    }

    public void Render(int windowWidth, int windowHeight)
    {
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.ClearColor(0.08f, 0.08f, 0.08f, 1.0f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        float leftX = -0.98f; // top-left NDC x
        float y = 0.75f;      // lowered from 0.95f for better vertical balance
        // Use a fixed scale for all menu items
        float scaleTitle = 1.6f;
        float scaleOption = 1.3f;
        float scaleInfo = 1.1f;
        float scaleSub = 1.2f;
        // Use a helper to get text height in NDC for spacing
        float GetTextHeight(string text, float scale)
        {
            using (var bmp = new System.Drawing.Bitmap(512, 64))
            using (var gfx = System.Drawing.Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, 32, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
            {
                var textSize = gfx.MeasureString(text, font);
                return (float)textSize.Height / windowHeight * 2.0f * scale;
            }
        }
        if (State == MenuState.Main)
        {
            DrawMenuText("==== VOXEL GAME ====", leftX, y, scaleTitle, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight("==== VOXEL GAME ====", scaleTitle) * 1.1f;
            string[] options = { "Play World", "Create New World", "Delete World", "Settings", "Exit" };
            for (int i = 0; i < options.Length; i++)
            {
                System.Drawing.Color color = (i == SelectedIndex) ? System.Drawing.Color.Yellow : System.Drawing.Color.White;
                string optText = (i == SelectedIndex ? "> " : "  ") + options[i];
                DrawMenuText(optText, leftX + 0.04f, y, scaleOption, color, windowWidth, windowHeight);
                y -= GetTextHeight(optText, scaleOption) * 1.2f;
            }
        }
        else if (State == MenuState.CreateWorld)
        {
            DrawMenuText("Enter new world name: " + NewWorldName, leftX, y, scaleOption, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight("Enter new world name: " + NewWorldName, scaleOption) * 1.1f;
            DrawMenuText("(A-Z, 0-9, Backspace, Enter to confirm, Esc to cancel)", leftX, y, scaleInfo, System.Drawing.Color.LightGray, windowWidth, windowHeight);
        }
        else if (State == MenuState.SelectWorld)
        {
            DrawMenuText("Select world to play:", leftX, y, scaleOption, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight("Select world to play:", scaleOption) * 1.1f;
            for (int i = 0; i < WorldList.Count; i++)
            {
                System.Drawing.Color color = (i == SelectedWorldIndex) ? System.Drawing.Color.Yellow : System.Drawing.Color.White;
                string optText = (i == SelectedWorldIndex ? "> " : "  ") + WorldList[i];
                DrawMenuText(optText, leftX + 0.04f, y, scaleSub, color, windowWidth, windowHeight);
                y -= GetTextHeight(optText, scaleSub) * 1.1f;
            }
            DrawMenuText("(Enter to play, Esc to cancel)", leftX, y, scaleInfo, System.Drawing.Color.LightGray, windowWidth, windowHeight);
        }
        else if (State == MenuState.DeleteWorld)
        {
            DrawMenuText("Select world to delete:", leftX, y, scaleOption, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight("Select world to delete:", scaleOption) * 1.1f;
            for (int i = 0; i < WorldList.Count; i++)
            {
                System.Drawing.Color color = (i == SelectedWorldIndex) ? System.Drawing.Color.Red : System.Drawing.Color.White;
                string optText = (i == SelectedWorldIndex ? "> " : "  ") + WorldList[i];
                DrawMenuText(optText, leftX + 0.04f, y, scaleSub, color, windowWidth, windowHeight);
                y -= GetTextHeight(optText, scaleSub) * 1.1f;
            }
            DrawMenuText("(Enter to delete, Esc to cancel)", leftX, y, scaleInfo, System.Drawing.Color.LightGray, windowWidth, windowHeight);
        }
        else if (State == MenuState.ConfirmDelete)
        {
            DrawMenuText($"Delete world '{DeleteCandidate}'? (Y/N)", leftX, y, scaleOption, System.Drawing.Color.Red, windowWidth, windowHeight);
        }
        else if (State == MenuState.Settings)
        {
            DrawMenuText("==== SETTINGS ====", leftX, y, scaleOption, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight("==== SETTINGS ====", scaleOption) * 1.1f;
            DrawMenuText($"Render Distance: {MenuSettings.RenderDistance}  (Left/Right to change, Enter to save)", leftX, y, scaleSub, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight($"Render Distance: {MenuSettings.RenderDistance}  (Left/Right to change, Enter to save)", scaleSub) * 1.1f;
            DrawMenuText($"Vertical Render Distance: {MenuSettings.VerticalRenderDistance}  (Up/Down to change)", leftX, y, scaleSub, System.Drawing.Color.White, windowWidth, windowHeight);
            y -= GetTextHeight($"Vertical Render Distance: {MenuSettings.VerticalRenderDistance}  (Up/Down to change)", scaleSub) * 1.1f;
            DrawMenuText("(Esc to return to main menu)", leftX, y, scaleInfo, System.Drawing.Color.LightGray, windowWidth, windowHeight);
        }
        // Draw transient message if present
        if (!string.IsNullOrEmpty(MenuMessage))
        {
            DrawMenuText(MenuMessage, leftX, -0.8f, scaleInfo, System.Drawing.Color.OrangeRed, windowWidth, windowHeight);
        }
    }
    private static Shader? _menuTextShader;
    private const string _menuTextVertexSource = @"#version 330 core
layout(location = 0) in vec2 aPos;
layout(location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);
    vTexCoord = aTexCoord;
}";
    private const string _menuTextFragmentSource = @"#version 330 core
in vec2 vTexCoord;
uniform sampler2D tex;
out vec4 FragColor;
void main() {
    FragColor = texture(tex, vTexCoord);
}";

    // Helper for OpenGL text rendering (with cache)
    private void DrawMenuText(string text, float x, float y, float scale, System.Drawing.Color color, int windowWidth, int windowHeight)
    {
        var key = (text, scale, color);
        if (!_textCache.TryGetValue(key, out var entry))
        {
            int baseFontSize = 32;
            using (var tmpBmp = new System.Drawing.Bitmap(1, 1))
            using (var tmpGfx = System.Drawing.Graphics.FromImage(tmpBmp))
            using (var font = new System.Drawing.Font(System.Drawing.FontFamily.GenericSansSerif, baseFontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel))
            {
                var textSize = tmpGfx.MeasureString(text, font);
                int bmpWidth = (int)Math.Ceiling(textSize.Width) + 8;
                int bmpHeight = (int)Math.Ceiling(textSize.Height) + 8;
                using (var bmp = new System.Drawing.Bitmap(bmpWidth, bmpHeight))
                using (var gfx = System.Drawing.Graphics.FromImage(bmp))
                using (var brush = new System.Drawing.SolidBrush(color))
                {
                    gfx.Clear(System.Drawing.Color.Transparent);
                    gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    gfx.DrawString(text, font, brush, new System.Drawing.PointF(0, 0));
                    var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
                        System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    int tex = GL.GenTexture();
                    GL.BindTexture(TextureTarget.Texture2D, tex);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0,
                        OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                    GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                    bmp.UnlockBits(data);
                    entry = new TextCacheEntry
                    {
                        TextureId = tex,
                        Width = bmp.Width,
                        Height = bmp.Height,
                        Scale = scale,
                        Text = text,
                        Color = color,
                        LastFrameUsed = _frameCounter
                    };
                    _textCache[key] = entry;
                }
            }
        }
        else
        {
            entry.LastFrameUsed = _frameCounter;
        }
        // Map bitmap size to NDC, then apply scale to the quad only
        float w = (float)entry.Width / windowWidth * 2.0f * scale;
        float h = (float)entry.Height / windowHeight * 2.0f * scale;
        float[] verts = {
            x,     y,      0, 0,
            x+w,   y,      1, 0,
            x+w,   y-h,    1, 1,
            x,     y-h,    0, 1
        };
        uint[] inds = { 0, 1, 2, 2, 3, 0 };
        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        int ebo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, inds.Length * sizeof(uint), inds, BufferUsageHint.DynamicDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        if (_menuTextShader == null)
            _menuTextShader = new Shader(_menuTextVertexSource, _menuTextFragmentSource);
        _menuTextShader.Use();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, entry.TextureId);
        GL.Uniform1(GL.GetUniformLocation(_menuTextShader.Handle, "tex"), 0);
        GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
        GL.BindVertexArray(0);
        GL.DeleteBuffer(vbo);
        GL.DeleteBuffer(ebo);
        GL.DeleteVertexArray(vao);
        // Do NOT delete the texture here! It is cached.
        // Periodically clean up old textures
        if (_frameCounter % 60 == 0)
        {
            var toRemove = _textCache.Where(kv => _frameCounter - kv.Value.LastFrameUsed > TextCacheMaxAge).ToList();
            foreach (var kv in toRemove)
            {
                GL.DeleteTexture(kv.Value.TextureId);
                _textCache.Remove(kv.Key);
            }
        }
    }

    public class Settings
    {
        public int RenderDistance = 2;
        public int VerticalRenderDistance = 2;
        private static readonly string SettingsPath = "settings";

        public void Save()
        {
            File.WriteAllText(SettingsPath, $"RenderDistance={RenderDistance}\nVerticalRenderDistance={VerticalRenderDistance}\n");
        }

        public void Load()
        {
            if (!File.Exists(SettingsPath)) return;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                var parts = line.Split('=');
                if (parts.Length != 2) continue;
                if (parts[0] == "RenderDistance" && int.TryParse(parts[1], out int val))
                    RenderDistance = val;
                if (parts[0] == "VerticalRenderDistance" && int.TryParse(parts[1], out int vval))
                    VerticalRenderDistance = vval;
            }
        }
    }

    public class VoxelWindow : GameWindow
    {
        private const int ChunkSize = 16;
        public static string CurrentWorldName = "default"; // Set to selected world in menu
        public static int worldHeight = 256; // Default world height, can be changed in settings
        public Settings Settings = new Settings();
        Vector3 direction = Vector3.Zero;
        private int _vao, _vbo;
        private Camera? _camera;
        // private Chunk _chunk;
        private Shader? _shaderProgram;
        public static int CurrentVBO;
        public int gamemode = 2; // 0 = survival, 1 = creative 2 = spectator
        private Vector2 _lastMousePos;
        private bool _firstMove = true;
        private float _cameraSpeed = 1.0f; // units per second
        private float _sensitivity = 0.2f;
        public Vector3 playerPos = (8.0f, 100, 8.0f);
        public float friction = 0.9f;
        private int _crosshairVAO, _crosshairVBO;
        // Highlight box state
        private int _highlightVAO, _highlightVBO;
        private int _highlightBoxVAO, _highlightBoxVBO; // NEW: for thick highlight
        public Shader? _highlightShader;
        private Vector3? _highlightBlock = null;
        public int RenderDistance = 7; // Default render distance, can be changed in settings
        public int VerticalRenderDistance = 4; // Default vertical render distance

        // --- Chunk worker fields ---
        private readonly ConcurrentQueue<(int, int, int)> _chunkLoadQueue = new();
        private readonly ConcurrentQueue<(Chunk, float[])> _meshIntegrationQueue = new();
        private readonly HashSet<(int, int, int)> _chunksLoading = new();
        private readonly int _chunkWorkerCount = Math.Max(2, Environment.ProcessorCount - 1);
        private readonly List<Thread> _chunkWorkers = new();
        private volatile bool _chunkWorkersRunning = true;

        // --- Chunk mesh data for rendering (main thread only) ---
        private Dictionary<(int, int, int), float[]> _chunkMeshes = new();

        // --- Chunk management ---
        private Dictionary<(int, int, int), Chunk> _chunks = new();
        private readonly object _chunksLock = new();
  
        private double _physicsAccumulator = 0;
        private const double PhysicsTimeStep = 1.0 / 20.0; // 20 Hz physics update

        public VoxelWindow(GameWindowSettings gws, NativeWindowSettings nws) : base(gws, nws)
        {
            CenterWindow(new Vector2i(1280, 720));
        }

        // Loads block textures into _blockTextures dictionary for use in hotbar and world rendering
        private Dictionary<int, int> _blockTextures = new Dictionary<int, int>();
        private void LoadBlockTextures()
        {
            _blockTextures.Clear();
            foreach (var block in BlockRegistry.Blocks.Values)
            {
                if (!string.IsNullOrWhiteSpace(block.texture) && File.Exists(block.texture))
                {
                    int texId = LoadTexture(block.texture);
                    _blockTextures[block.id] = texId;
                }
            }
        }

        private Shader? _crosshairShader;

        private bool IsColliding(Vector3 position)
        {
            Vector3 halfExtents = new Vector3(0.25f, 1.8f, 0.25f); // AABB around camera
            Vector3 min = position - halfExtents;
            Vector3 max = position + new Vector3(0.25f, 0.1f, 0.25f);
            for (int x = (int)MathF.Floor(min.X); x <= (int)MathF.Floor(max.X); x++)
                for (int y = (int)MathF.Floor(min.Y); y <= (int)MathF.Floor(max.Y); y++)
                    for (int z = (int)MathF.Floor(min.Z); z <= (int)MathF.Floor(max.Z); z++)
                        if (IsBlockAt(x, y, z))
                            return true;
            return false;
        }
        private bool IsTouchingGround()
        {
            // Use playerPos as the player's head, feet at playerPos.Y - 1.8f
            float feetY = playerPos.Y;
            float halfWidth = 0.25f;
            float epsilon = 0.05f; // small offset below feet
            float y = feetY - epsilon;
            // Check 3x3 grid under feet for ground
            for (float dx = -halfWidth; dx <= halfWidth; dx += halfWidth)
            {
                for (float dz = -halfWidth; dz <= halfWidth; dz += halfWidth)
                {
                    int bx = (int)MathF.Floor(playerPos.X + dx);
                    int by = (int)MathF.Floor(y);
                    int bz = (int)MathF.Floor(playerPos.Z + dz);
                    if (IsBlockAt(bx, by, bz))
                        return true;
                }
            }
            return false;
        }


        private void SetupCrosshair()
        {
            float[] crosshairVertices = new float[]
            {
            // Vertical line (centered)
            0.0f,  0.02f,
            0.0f, -0.02f,
            // Horizontal line (centered)
            -0.02f, 0.0f,
            0.02f, 0.0f
            };

            _crosshairVAO = GL.GenVertexArray();
            _crosshairVBO = GL.GenBuffer();

            GL.BindVertexArray(_crosshairVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _crosshairVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, crosshairVertices.Length * sizeof(float), crosshairVertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            _crosshairShader = new Shader(crosshairVertexSource, crosshairFragmentSource);
        }

        private void SetupHighlightBox()
        {
            float[] boxLines = {
                // Bottom square
                0,0,0, 1,0,0,
                1,0,0, 1,0,1,
                1,0,1, 0,0,1,
                0,0,1, 0,0,0,
                // Top square
                0,1,0, 1,1,0,
                1,1,0, 1,1,1,
                1,1,1, 0,1,1,
                0,1,1, 0,1,0,
                // Vertical lines
                0,0,0, 0,1,0,
                1,0,0, 1,1,0,
                1,0,1, 1,1,1,
                0,0,1, 0,1,1
            };
            _highlightVAO = GL.GenVertexArray();
            _highlightVBO = GL.GenBuffer();
            GL.BindVertexArray(_highlightVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _highlightVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, boxLines.Length * sizeof(float), boxLines, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // NEW: Setup highlight box (cube) for thick outline
            float[] boxQuads = {
                // 6 faces, 2 triangles per face, 3 vertices per triangle
                // Each face: 4 vertices (quad), but triangles: 0-1-2, 2-3-0
                // Front (+Z)
                0,0,1, 1,0,1, 1,1,1, 0,1,1,
                // Back (-Z)
                0,0,0, 1,0,0, 1,1,0, 0,1,0,
                // Left (-X)
                0,0,0, 0,0,1, 0,1,1, 0,1,0,
                // Right (+X)
                1,0,0, 1,0,1, 1,1,1, 1,1,0,
                // Top (+Y)
                0,1,0, 1,1,0, 1,1,1, 0,1,1,
                // Bottom (-Y)
                0,0,0, 1,0,0, 1,0,1, 0,0,1
            };
            uint[] boxIndices = {
                // Front
                0,1,2, 2,3,0,
                // Back
                4,5,6, 6,7,4,
                // Left
                8,9,10, 10,11,8,
                // Right
                12,13,14, 14,15,12,
                // Top
                16,17,18, 18,19,16,
                // Bottom
                20,21,22, 22,23,20
            };
            _highlightBoxVAO = GL.GenVertexArray();
            _highlightBoxVBO = GL.GenBuffer();
            int boxEBO = GL.GenBuffer();
            GL.BindVertexArray(_highlightBoxVAO);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _highlightBoxVBO);
            GL.BufferData(BufferTarget.ArrayBuffer, boxQuads.Length * sizeof(float), boxQuads, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, boxEBO);
            GL.BufferData(BufferTarget.ElementArrayBuffer, boxIndices.Length * sizeof(uint), boxIndices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.BindVertexArray(0);

    _highlightShader = new Shader(highlightVertexSource, highlightFragmentSource);
}

        private bool _gameInitialized = false;
        private void InitGameWorld()
        {
            if (_gameInitialized) return;
            Settings.Load();
            BlockRegistry.Load("blocks.json");
            LoadBlockTextures();
            RenderDistance = Settings.RenderDistance;
            // Camera and world setup
            _camera = new Camera(new Vector3(0, 6, 0), Size.X / (float)Size.Y);
            _camera.Position = playerPos - new Vector3(0.5f, -1.7f, 0.5f);
            _camera.Centre = _camera.Position + new Vector3(0.5f, 0.5f, 0.5f);
            SetupCrosshair();
            // Load block textures
            _shaderProgram = new Shader(VertexShaderSource, FragmentShaderSource);
            _shaderProgram.Use();
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
            // Vertex attributes: position (3), color (3), uv (2), blockType (1) = 9 floats
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 9 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 9 * sizeof(float), 8 * sizeof(float));
            GL.EnableVertexAttribArray(3);
            GL.BindVertexArray(0); // Unbind after setup

            GL.Viewport(0, 0, Size.X, Size.Y);


          

            // Set a dark background for menu visibility
            GL.ClearColor(0.08f, 0.08f, 0.08f, 1.0f); // dark gray

            // Highlight box setup
            SetupHighlightBox();
            _gameInitialized = true;
            LoadPlayerData();

            // Initialize camera if null
            if (_camera == null)
            {
                _camera = new Camera(playerPos - new Vector3(0.5f, 0.5f, 0.5f), Size.X / (float)Size.Y);
            }
            // Start chunk workers if not running
            if (_chunkWorkers.Count == 0)
            {
                StartChunkWorkers();
            }
            // No forced initial chunk generation or debug logging
            _gameInitialized = true;
            GenerateInitialChunks(2);
        }


        protected override void OnLoad()
        {
            base.OnLoad();
            Settings.Load();
            CurrentVBO = _vbo;
            CursorState = CursorState.Grabbed;

            
            GL.Enable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace); // Disable face culling so all faces are visible
                                            // Do not initialize world/camera/chunks here!
                                            // Only set up menu state
            _gameInitialized = false;
            
        }

        //some things for controlls 


        bool spacePressed = false;
        int spaceTimer = 0;
        bool leftClickPressed = false;
        int leftClickTimer = 0;
        bool rightClickPressed = false;
        int rightClickTimer = 0;
        bool ePressed = false;
        int eTimer = 0;

        private Inventory _inventory = new Inventory();
        private bool _inventoryOpen = false;

        private double _lastAutoSaveTime = 0;
        private const double AutoSaveInterval = 120.0; // seconds (2 min, adjust as needed)

        private GameState _gameState = GameState.MainMenu;
        private MainMenu _mainMenu = new MainMenu();
        
        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            IntegrateChunkMeshes();
            _physicsAccumulator += args.Time;
            // Only update game logic/physics at fixed 20 Hz
            while (_physicsAccumulator >= PhysicsTimeStep)
            {
                UpdateGameLogic(PhysicsTimeStep);
                _physicsAccumulator -= PhysicsTimeStep;
            }
            // Set cursor visibility based on game state
            if (_gameState == GameState.MainMenu)
                CursorState = CursorState.Normal;
            else if (_gameState == GameState.Playing)
                CursorState = CursorState.Grabbed;
        }

        // Extracted game logic from OnUpdateFrame for fixed timestep
        private void UpdateGameLogic(double dt)
        {
            if (_gameState == GameState.MainMenu)
            {
                _mainMenu.Update(KeyboardState, MouseState, Size.X, Size.Y);
                if (_mainMenu.StartGameSelected)
                {
                    InitGameWorld();
                    _gameState = GameState.Playing;
                }
                else if (_mainMenu.ExitSelected)
                {
                    _gameState = GameState.Exiting;
                    save();
                    Close();
                }
                return;
            }
            _lastAutoSaveTime += dt;
            if (_lastAutoSaveTime >= AutoSaveInterval)
            {
                save();
                _lastAutoSaveTime = 0;
            }
            var input = KeyboardState;
            if (input.IsKeyDown(Keys.Escape))
            {
                save();
                Close();
            }
            UnloadFarChunks();
            LoadNearbyChunks();
            if (spacePressed)
            {
                spaceTimer += 1;
                if (spaceTimer >= 10)
                {
                    spacePressed = false;
                    spaceTimer = 0;
                }
            }
            if (_camera == null) return;
            _camera.Position = playerPos - new Vector3(0.5f, -1.7f, 0.5f);
            _camera.Centre = _camera.Position + new Vector3(0.5f, 0.5f, 0.5f);
            float cameraSpeed = _cameraSpeed;
            if (gamemode == 0)
                direction = (direction[0] * friction, direction[1], direction[2] * friction);
            else
                direction = Vector3.Zero;
            if (input.IsKeyDown(Keys.LeftControl))
                cameraSpeed = _cameraSpeed * 2;
            if (input.IsKeyDown(Keys.W))
                direction += (_camera.Rotation[0] * cameraSpeed, 0, _camera.Rotation[2] * cameraSpeed);
            if (input.IsKeyDown(Keys.S))
                direction -= (_camera.Rotation[0] * cameraSpeed, 0, _camera.Rotation[2] * cameraSpeed);
            if (input.IsKeyDown(Keys.A))
                direction -= (_camera.Right[0] * cameraSpeed, 0, _camera.Right[2] * cameraSpeed);
            if (input.IsKeyDown(Keys.D))
                direction += (_camera.Right[0] * cameraSpeed, 0, _camera.Right[2] * cameraSpeed);
            if (input.IsKeyDown(Keys.Space) && IsTouchingGround() && gamemode == 0 && !spacePressed)
            {
                direction += (0, 10, 0);
                spacePressed = true;
            }
            else if (input.IsKeyDown(Keys.Space) && gamemode != 0)
                direction += (0, 1, 0);
            if (input.IsKeyDown(Keys.LeftShift))
                direction -= (0, 1, 0);
            if (gamemode == 0 && !IsTouchingGround())
                direction -= (0, 0.6f, 0);
            if (gamemode != 2 && direction[1] < 0 && IsTouchingGround())
                direction[1] = 0;
            if (ePressed)
            {
                eTimer += 1;
                if (eTimer >= 10)
                {
                    ePressed = false;
                    eTimer = 0;
                }
            }
            if (input.IsKeyDown(Keys.E) && !ePressed)
            {
                ePressed = true;
                if (!_inventoryOpen)
                {
                    _inventoryOpen = true;
                }
                else
                {
                    _inventoryOpen = false;
                }
            }
            if (input.IsKeyDown(Keys.R))
            {
                Console.WriteLine(playerPos);
                Console.WriteLine(_camera.Centre);
                Console.WriteLine(IsTouchingGround());
            }
            if (_camera.Position[1] <= -20)
            {
                playerPos = (8.0f, 20.0f, 8.0f);
                direction = (0, 0, 0);
            }
            if (direction[0] < 0.01f && direction[0] > -0.01f && direction[0] != 0)
                direction[0] = 0;
            if (direction[1] < 0.01f && direction[1] > -0.01f && direction[1] != 0)
                direction[1] = 0;
            if (direction[2] < 0.01f && direction[2] > -0.01f && direction[2] != 0)
                direction[2] = 0;
            Vector3 newPos = playerPos - new Vector3(0.0f, -1.8f, 0.0f);
            Vector3 delta = direction * (float)dt;
            if (gamemode == 2)
            {
                newPos += delta * 20.0f;
                playerPos = newPos + new Vector3(0.0f, -1.8f, 0.0f);
                _camera.Position = newPos - new Vector3(0.5f, 0.5f, 0.5f);
                _camera.Centre = _camera.Position + new Vector3(0.5f, 0.5f, 0.5f);
            }
            else
            {
                int steps = (int)Math.Ceiling(delta.Length / 0.1f);
                Vector3 step = delta / steps;
                for (int i = 0; i < steps; i++)
                {
                    Vector3 tryX = newPos + new Vector3(step.X, 0, 0);
                    if (!IsColliding(tryX))
                        newPos = tryX;
                    else
                        direction[0] = 0;
                    Vector3 tryY = newPos + new Vector3(0, step.Y, 0);
                    if (!IsColliding(tryY))
                        newPos = tryY;
                    else
                        direction[1] = 0;
                    Vector3 tryZ = newPos + new Vector3(0, 0, step.Z);
                    if (!IsColliding(tryZ))
                        newPos = tryZ;
                    else
                        direction[2] = 0;
                }
                playerPos = newPos + new Vector3(0.0f, -1.8f, 0.0f);
                _camera.Position = newPos - new Vector3(0.5f, 0.5f, 0.5f);
                _camera.Centre = _camera.Position + new Vector3(0.5f, 0.5f, 0.5f);
            }
            if (gamemode != 2 && IsTouchingGround() && direction[1] >= 0)
            {
                Vector3 footPos = _camera.Centre - new Vector3(0, 1.85f, 0);
                float halfWidth = 0.25f;
                float yBelow = footPos.Y - 0.01f;
                int maxBlockY = int.MinValue;
                for (float dx = -halfWidth; dx <= halfWidth; dx += halfWidth)
                {
                    for (float dz = -halfWidth; dz <= halfWidth; dz += halfWidth)
                    {
                        int bx = (int)MathF.Floor(footPos.X + dx);
                        int by = (int)MathF.Floor(yBelow);
                        int bz = (int)MathF.Floor(footPos.Z + dz);
                        if (IsBlockAt(bx, by, bz) && by > maxBlockY)
                            maxBlockY = by;
                    }
                }
                if (maxBlockY != int.MinValue)
                {
                    playerPos.Y = maxBlockY + 1.0f;
                }
            }
            // Mouse input for camera rotation
            var mouse = MouseState;
            if (_firstMove)
            {
                _lastMousePos = new Vector2(mouse.X, mouse.Y);
                _firstMove = false;
            }
            else
            {
                float deltaX = mouse.X - _lastMousePos.X;
                float deltaY = mouse.Y - _lastMousePos.Y;
                _lastMousePos = new Vector2(mouse.X, mouse.Y);

                _camera.Yaw += deltaX * _sensitivity;
                _camera.Pitch -= deltaY * _sensitivity; // invert Y axis

                _camera.Pitch = MathHelper.Clamp(_camera.Pitch, -89f, 89f);
            }
            if (leftClickPressed)
            {
                leftClickTimer += 1;
                if (leftClickTimer >= 10)
                {
                    leftClickTimer = 0;
                    leftClickPressed = false;
                }
            }


            if (mouse.IsButtonDown(MouseButton.Left) && !leftClickPressed)
            {

                if (Raycast(out var hitBlock, out _, 6f))
                {
                    leftClickPressed = true;
                    int brokenBlockType = GetBlockType((int)hitBlock.X, (int)hitBlock.Y, (int)hitBlock.Z);
                    BreakBlock((int)hitBlock.X, (int)hitBlock.Y, (int)hitBlock.Z);
                    if (brokenBlockType >= 0)
                    {
                        _inventory.AddBlock(brokenBlockType, 1);
                    }
                }
            }
            if (rightClickPressed)
            {
                rightClickTimer += 1;
                if (rightClickTimer >= 10)
                {
                    rightClickTimer = 0;
                    rightClickPressed = false;
                }

            }
            // Hotbar selection (number keys 1-9)
            for (int i = 0; i < Inventory.HotbarSize; i++)
            {
                if (KeyboardState.IsKeyDown((Keys)((int)Keys.D1 + i)))
                {
                    _inventory.SelectedHotbarSlot = i;
                }
            }
            // Mouse wheel for hotbar
            float scroll = MouseState.ScrollDelta.Y;
            if (scroll != 0)
            {
                _inventory.SelectedHotbarSlot = (_inventory.SelectedHotbarSlot - (int)scroll + Inventory.HotbarSize) % Inventory.HotbarSize;
            }
            if (mouse.IsButtonDown(MouseButton.Right) && !rightClickPressed)
            {
                if (Raycast(out var hitBlockPlace, out var normal, 6f))
                {
                    var roundedHitBlockPlace = (MathF.Round(hitBlockPlace.X), MathF.Round(hitBlockPlace.Y), MathF.Round(hitBlockPlace.Z));
                    var roundedNormal = (MathF.Round(normal.X), MathF.Round(normal.Y), MathF.Round(normal.Z));
                    rightClickPressed = true;
                    Vector3 placePos = new Vector3(roundedHitBlockPlace.Item1, MathF.Round(hitBlockPlace.Y), roundedHitBlockPlace.Item3) - new Vector3(roundedNormal.Item1, roundedNormal.Item2, roundedNormal.Item3);
                    Vector3 halfExtents = new Vector3(0.25f, 1.8f, 0.25f);
                    Vector3 playerMin = playerPos - (halfExtents.X, 0, halfExtents.Z);
                    Vector3 playerMax = playerPos + halfExtents;
                    Vector3 blockMin = new Vector3(
                        (int)MathF.Floor(placePos.X),
                        (int)MathF.Floor(placePos.Y),
                        (int)MathF.Floor(placePos.Z)
                    );
                    Vector3 blockMax = blockMin + Vector3.One;
                    bool overlaps =
                        playerMin.X < blockMax.X && playerMax.X > blockMin.X &&
                        playerMin.Y < blockMax.Y && playerMax.Y > blockMin.Y &&
                        playerMin.Z < blockMax.Z && playerMax.Z > blockMin.Z;

                    if (placePos.Y >= 0 && placePos.Y < worldHeight)
                    {
                        if (!overlaps)
                        {
                            var selected = _inventory.GetSelectedItem();
                            if (selected.Count > 0)
                            {
                                PlaceBlock(
                                    (int)MathF.Floor(placePos.X),
                                    (int)MathF.Floor(placePos.Y),
                                    (int)MathF.Floor(placePos.Z),
                                    selected.BlockId
                                );
                                _inventory.RemoveBlock(selected.BlockId, 1);
                            }
                        }
                    }
                }
            }

            // Highlight block detection
            if (Raycast(out var highlightHitBlock, out _, 6f))
                _highlightBlock = highlightHitBlock;
            else
                _highlightBlock = null;

            _camera.UpdateVectors();
        }
        private void InitSkybox()
        {
            if (_skyboxInitialized) return;
            float[] skyboxVertices = {
                // positions for a cube (no texcoords needed for gradient)
                -1,  1, -1,  -1, -1, -1,   1, -1, -1,   1, -1, -1,   1,  1, -1,  -1,  1, -1, // back
                -1, -1,  1,  -1, -1, -1,  -1,  1, -1,  -1,  1, -1,  -1,  1,  1,  -1, -1,  1, // left
                1, -1, -1,   1, -1,  1,   1,  1,  1,   1,  1,  1,   1,  1, -1,   1, -1, -1, // right
                -1, -1,  1,  -1,  1,  1,   1,  1,  1,   1,  1,  1,   1, -1,  1,  -1, -1,  1, // front
                -1,  1, -1,   1,  1, -1,   1,  1,  1,   1,  1,  1,  -1,  1,  1,  -1,  1, -1, // top
                -1, -1, -1,  -1, -1,  1,   1, -1,  1,   1, -1,  1,   1, -1, -1,  -1, -1, -1  // bottom
            };
            _skyboxVao = GL.GenVertexArray();
            _skyboxVbo = GL.GenBuffer();
            GL.BindVertexArray(_skyboxVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _skyboxVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, skyboxVertices.Length * sizeof(float), skyboxVertices, BufferUsageHint.StaticDraw);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            _skyboxShader = new Shader(_skyboxVertexSource, _skyboxFragmentSource);
            _skyboxInitialized = true;
        }
        float time = 0.01f;
        int framecount = 0;
        float minfps = 0.0f;
        // Update rendering to draw all visible chunks
        protected override void OnRenderFrame(FrameEventArgs args)
        {
            framecount++;
            time += (float)args.Time;
            if (minfps == 0.0f) // Initialize minfps on first frame
                minfps = 1.0f / (float)args.Time;
            minfps = Math.Min(minfps, 1.0f / (float)args.Time);
            if (time >= 1.0f)
            {

                Console.WriteLine($"FPS: {framecount}\nminFPS: {minfps}");
                framecount = 0;
                time = 0.0f;
                minfps = 0.0f;
            }
            if (_gameState == GameState.MainMenu)
            {
                _mainMenu.Render(Size.X, Size.Y);
                SwapBuffers(); // Ensure menu is displayed with correct background
                return;
            }

            base.OnRenderFrame(args);
            InitSkybox();
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GL.DepthMask(false); // Disable depth writing
            GL.DepthFunc(DepthFunction.Lequal); // Allow skybox to pass depth test at far plane
            if (_skyboxShader != null && _camera != null)
            {
                _skyboxShader.Use();
                // Remove translation from view matrix
                Matrix4 skyboxView = _camera.GetViewMatrix();
                skyboxView.Row3.X = 0;
                skyboxView.Row3.Y = 0;
                skyboxView.Row3.Z = 0;
                skyboxView.Row3.W = 1;
                Matrix4 skyboxProjection = _camera.GetProjectionMatrix();
                int viewLoc = GL.GetUniformLocation(_skyboxShader.Handle, "view");
                int projLoc = GL.GetUniformLocation(_skyboxShader.Handle, "projection");
                GL.UniformMatrix4(viewLoc, false, ref skyboxView);
                GL.UniformMatrix4(projLoc, false, ref skyboxProjection);
                GL.BindVertexArray(_skyboxVao);
                GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
                GL.BindVertexArray(0);
            }
            GL.DepthMask(true); // Re-enable depth writing
            GL.DepthFunc(DepthFunction.Less); // Restore default depth function

            if (_shaderProgram == null) return;
            _shaderProgram.Use();
            // Bind all loaded block textures to texture units 0..N
            int maxBlockId = 16;
            foreach (var id in _blockTextures.Keys)
                if (id > maxBlockId) maxBlockId = id;
            int maxTextures = 32; // Must match shader
            int[] textureUnits = new int[maxTextures];
            for (int i = 0; i < maxTextures; i++)
            {
                if (_blockTextures.TryGetValue(i, out int texId))
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GL.BindTexture(TextureTarget.Texture2D, texId);
                    textureUnits[i] = i;
                }
                else
                {
                    // Bind a fallback texture (e.g., air or magenta)
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    GL.BindTexture(TextureTarget.Texture2D, 0); // 0 = no texture
                    textureUnits[i] = i;
                }
            }
            // Set the uniform array for blockTextures
            int blockTexturesLoc = GL.GetUniformLocation(_shaderProgram.Handle, "blockTextures");
            GL.Uniform1(blockTexturesLoc, maxTextures, textureUnits);
            // Bind the correct block texture for each chunk draw, using _blockTextures and blockType attribute
            // Remove legacy _grassTex/_dirtTex binding
            Matrix4 model = Matrix4.Identity;
            if (_camera == null) return;
            Matrix4 view = _camera.GetViewMatrix();
            Matrix4 projection = _camera.GetProjectionMatrix();
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram.Handle, "model"), false, ref model);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram.Handle, "view"), false, ref view);
            GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram.Handle, "projection"), false, ref projection);
            // For each chunk, draw with the correct block texture per blockType

            foreach (var kv in _chunks)
            {
                var chunkCoord = kv.Key;
                var chunk = kv.Value;
                if (chunk.VertexCount == 0) continue;
                if (chunk.VaoHandle == 0) continue; // Skip empty meshes or missing VAO
                Vector3 chunkWorldPos = new Vector3(chunkCoord.Item1 * ChunkSize, chunkCoord.Item2 * ChunkSize, chunkCoord.Item3 * ChunkSize);
                Matrix4 chunkModel = Matrix4.CreateTranslation(chunkWorldPos);
                GL.UniformMatrix4(GL.GetUniformLocation(_shaderProgram.Handle, "model"), false, ref chunkModel);
                GL.BindVertexArray(chunk.VaoHandle);
                GL.DrawArrays(PrimitiveType.Triangles, 0, chunk.VertexCount);
            }

            // === Render crosshair (2D) ===
            GL.Disable(EnableCap.DepthTest); // disable depth for 2D overlay
            if (_crosshairShader == null) return;
            _crosshairShader.Use();
            GL.BindVertexArray(_crosshairVAO);
            GL.DrawArrays(PrimitiveType.Lines, 0, 4);
            GL.Enable(EnableCap.DepthTest); // re-enable depth for next frame

            // === Render highlight box ===
            if (_highlightBlock.HasValue)
            {
                if (_highlightShader == null) return;
                _highlightShader.Use();
                float expand = 0.04f; // Slightly larger for thick outline
                Vector3 blockPos = _highlightBlock.Value - new Vector3(0.5f + expand / 2, 0.5f + expand / 2, 0.5f + expand / 2);
                Matrix4 highlightModel = Matrix4.CreateScale(1.0f + expand) * Matrix4.CreateTranslation(blockPos);
                Matrix4 highlightView = _camera.GetViewMatrix();
                Matrix4 highlightProjection = _camera.GetProjectionMatrix();
                GL.UniformMatrix4(GL.GetUniformLocation(_highlightShader.Handle, "model"), false, ref highlightModel);
                GL.UniformMatrix4(GL.GetUniformLocation(_highlightShader.Handle, "view"), false, ref highlightView);
                GL.UniformMatrix4(GL.GetUniformLocation(_highlightShader.Handle, "projection"), false, ref highlightProjection);

                // Draw thick box (transparent faces, both front and back faces)
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false); // Don't write to depth buffer
                GL.Enable(EnableCap.CullFace);
                // Draw back faces
                GL.CullFace(TriangleFace.Back);
                GL.BindVertexArray(_highlightBoxVAO);
                GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
                // Draw front faces
                GL.CullFace(TriangleFace.Front);
                GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.Disable(EnableCap.CullFace);
                GL.Disable(EnableCap.Blend);
                GL.DepthMask(true);

                // Draw thin wireframe box on top for clarity
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                GL.Disable(EnableCap.DepthTest); // Always visible
                float wireExpand = 0.001f; // Slightly larger to avoid z-fighting
                Vector3 wirePos = _highlightBlock.Value - new Vector3(0.5f + wireExpand / 2, 0.5f + wireExpand / 2, 0.5f + wireExpand / 2);
                Matrix4 wireModel = Matrix4.CreateScale(1.0f + wireExpand) * Matrix4.CreateTranslation(wirePos);
                GL.UniformMatrix4(GL.GetUniformLocation(_highlightShader.Handle, "model"), false, ref wireModel);
                GL.BindVertexArray(_highlightBoxVAO);
                GL.DrawElements(PrimitiveType.Triangles, 36, DrawElementsType.UnsignedInt, 0);
                GL.BindVertexArray(0);
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GL.Enable(EnableCap.DepthTest);
            }

            // === Render hotbar ===
            RenderHotbar();

            SwapBuffers();
        }

        // Simple placeholder for hotbar rendering (does nothing for now)
        private void RenderHotbar()
        {
            // You can implement actual hotbar rendering here later.
            // Render the hotbar as 9 rectangles at the bottom center of the screen
            GL.Disable(EnableCap.DepthTest);
            int hotbarSlots = Inventory.HotbarSize;
            float slotWidth = 0.08f; // NDC width
            float slotHeight = 0.13f; // NDC height
            float spacing = 0.012f;
            float totalWidth = hotbarSlots * slotWidth + (hotbarSlots - 1) * spacing;
            float startX = -totalWidth / 2f;
            float y = -0.92f; // Near bottom
            GL.ActiveTexture(TextureUnit.Texture0);
            // Bind correct texture for each block type
            for (int i = 0; i < hotbarSlots; i++)
            {
                float x = startX + i * (slotWidth + spacing);
                bool selected = (i == _inventory.SelectedHotbarSlot);
                Vector3 slotColor = selected ? new Vector3(1.0f, 1.0f, 0.3f) : new Vector3(0.2f, 0.2f, 0.2f);
                DrawHotbarQuad(x, y, slotWidth, slotHeight, slotColor);
                if (_inventory.Items[i].Count > 0)
                {
                    float iconMargin = 0.015f;
                    // Draw block icon as a textured quad for grass/dirt
                    int blockId = _inventory.Items[i].BlockId;
                    int iconTexture = 0;
                    if (_blockTextures.TryGetValue(blockId, out int texId))
                        iconTexture = texId;
                    if (iconTexture != 0)
                    {
                        // Draw a textured quad for the block icon
                        DrawHotbarBlockIcon(x + iconMargin, y + iconMargin, slotWidth - 2 * iconMargin, slotHeight - 2 * iconMargin, iconTexture);
                        // GL.DeleteTexture(iconTexture); // Don't delete here, keep loaded
                    }
                    // Draw stack count as a number in the bottom right
                    DrawHotbarCountText(_inventory.Items[i].Count, x + slotWidth - 0.035f, y + 0.01f, 0.03f, 0.045f);
                }
            }
            GL.Enable(EnableCap.DepthTest);
        }



        private int LoadTexture(string path)
        {
            using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
            var pixels = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixels);

            int handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);

            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            return handle;
        }
        protected override void OnResize(ResizeEventArgs e)
        {
            base.OnResize(e);

            // Tell OpenGL to use the new window size for rendering
            GL.Viewport(0, 0, Size.X, Size.Y);
            if (_camera != null)
            {
                _camera.AspectRatio = Size.X / (float)Size.Y;
            }


        }
        public struct Vector3Int
        {
            public int X, Y, Z;
            public Vector3Int(int x, int y, int z) => (X, Y, Z) = (x, y, z);
        }

        // In Raycast, replace _chunk usage with multi-chunk helpers
        private bool Raycast(out Vector3 hitBlock, out Vector3 normal, float maxDistance = 6f)
        {
            if (_camera == null) { hitBlock = Vector3.Zero; normal = Vector3.Zero; return false; }
            Vector3 rayOrigin = _camera.Centre;
            Vector3 rayDirection = Vector3.Normalize(_camera.Front);
            for (float t = 0; t < maxDistance; t += 0.1f)
            {
                Vector3 point = rayOrigin + rayDirection * t;
                Vector3Int blockPos = new Vector3Int(
                    (int)MathF.Floor(point.X),
                    (int)MathF.Floor(point.Y),
                    (int)MathF.Floor(point.Z)
                );
                if (IsBlockAt(blockPos.X, blockPos.Y, blockPos.Z))
                {
                    hitBlock = new Vector3(blockPos.X, blockPos.Y, blockPos.Z);
                    Vector3 prevPoint = rayOrigin + rayDirection * (t - 0.1f);
                    Vector3Int prevBlock = new Vector3Int(
                        (int)MathF.Floor(prevPoint.X),
                        (int)MathF.Floor(prevPoint.Y),
                        (int)MathF.Floor(prevPoint.Z)
                    );
                    normal = Vector3.Normalize(hitBlock - new Vector3(prevBlock.X, prevBlock.Y, prevBlock.Z));
                    return true;
                }
            }
            hitBlock = Vector3.Zero;
            normal = Vector3.Zero;
            return false;
        }
        private int _skyboxVao = 0, _skyboxVbo = 0;
        private Shader? _skyboxShader = null;
        private bool _skyboxInitialized = false;

        private const string _skyboxVertexSource = @"#version 330 core
        layout(location = 0) in vec3 aPos;
        uniform mat4 view;
        uniform mat4 projection;
        void main()
        {
            vec4 pos = projection * mat4(mat3(view)) * vec4(aPos, 1.0);
            gl_Position = pos.xyww;
        }";

        private const string _skyboxFragmentSource = @"#version 330 core
        out vec4 FragColor;
        void main()
        {
            // Simple vertical gradient: blue at top, light blue at horizon, white at bottom
            float t = gl_FragCoord.y / 900.0; // assume 900px window height, adjust as needed
            vec3 top = vec3(0.38, 0.62, 0.93); // sky blue
            vec3 mid = vec3(0.69, 0.87, 1.0); // light blue
            vec3 bot = vec3(1.0, 1.0, 1.0);   // white
            vec3 color = mix(mid, top, clamp((t-0.5)*2.0, 0.0, 1.0));
            color = mix(bot, color, clamp(t*2.0, 0.0, 1.0));
            FragColor = vec4(color, 1.0);
        }";

        private const string crosshairVertexSource = @"
#version 330 core
layout(location = 0) in vec2 aPosition;

void main()
{
    gl_Position = vec4(aPosition, 0.0, 1.0);
}
";

        private const string crosshairFragmentSource = @"
#version 330 core
out vec4 FragColor;

void main()
{
    FragColor = vec4(0.0, 0.0, 0.0, 1.0); // black
}
";

        private const string VertexShaderSource = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
layout(location = 1) in vec3 aColor;
layout(location = 2) in vec2 aTexCoord;
layout(location = 3) in float aBlockType;

out vec3 ourColor;
out vec2 TexCoord;
flat out int blockType;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

void main()
{
    gl_Position = projection * view * model * vec4(aPosition, 1.0);
    ourColor = aColor;
    TexCoord = aTexCoord;
    blockType = int(aBlockType + 0.5); // round to nearest int
}"
    ;

        private const string FragmentShaderSource = @"
#version 330 core
in vec2 TexCoord;
flat in int blockType;
out vec4 FragColor;

uniform sampler2D blockTextures[32]; // Support up to 32 block types

void main()
{
    if (blockType >= 0 && blockType < 32)
        FragColor = texture(blockTextures[blockType], TexCoord);
    else
        FragColor = vec4(1.0, 0.0, 1.0, 1.0); // magenta for unknown
}
";

        private const string highlightVertexSource = @"
#version 330 core
layout(location = 0) in vec3 aPosition;
uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;
void main()
{
    gl_Position = projection * view * model * vec4(aPosition, 1.0);
}
";
        private const string highlightFragmentSource = @"
#version 330 core
out vec4 FragColor;
void main()
{

    FragColor = vec4(1.0, 1.0, 0.0, 0.35); // semi-transparent yellow
}
";

        // --- Hotbar rendering resources ---
        private Shader? _hotbarShader;
        private int _hotbarVao, _hotbarVbo;
        private bool _hotbarInitialized = false;

        // Hotbar quad shader sources
       
        private const string hotbarVertexSource = @"
    #version 330 core
    layout(location = 0) in vec2 aPos;
    layout(location = 1) in vec3 aColor;
    out vec3 vColor;
    void main() {
        gl_Position = vec4(aPos, 0.0, 1.0);
        vColor = aColor;
    }";
        private const string hotbarFragmentSource = @"
    #version 330 core
    in vec3 vColor;
    out vec4 FragColor;
    void main() {
        FragColor = vec4(vColor, 1.0);
    }";
        private Shader? _hotbarTextShader;
        private const string hotbarTextVertexSource = @"#version 330 core
layout(location = 0) in vec2 aPos;

layout(location = 1) in vec2 aTexCoord;
out vec2 vTexCoord;
void main() {
    gl_Position = vec4(aPos, 0.0, 1.0);


    vTexCoord = aTexCoord;
}";
private const string hotbarTextFragmentSource = @"#version 330 core
in vec2 vTexCoord;
uniform sampler2D tex;
out vec4 FragColor;
void main() {
    FragColor = texture(tex, vTexCoord);
}";

        private void InitHotbarResources()
        {
            if (_hotbarInitialized) return;
            _hotbarShader = new Shader(hotbarVertexSource, hotbarFragmentSource);
            _hotbarVao = GL.GenVertexArray();
            _hotbarVbo = GL.GenBuffer();
            _hotbarInitialized = true;
        }

        private void DrawHotbarQuad(float x, float y, float w, float h, Vector3 color)
        {
            InitHotbarResources();
            float[] vertices = {
            x,     y,     color.X, color.Y, color.Z,
                                            
                       x + w, y,     color.X, color.Y, color.Z,
            x + w, y + h, color.X, color.Y, color.Z,
            x,     y + h, color.X, color.Y, color.Z
        };
            uint[] indices = { 0, 1, 2, 2, 3, 0 };
            int ebo = GL.GenBuffer();
            GL.BindVertexArray(_hotbarVao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, _hotbarVbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.DynamicDraw);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 5 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 5 * sizeof(float), 2 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            _hotbarShader?.Use();
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            GL.DeleteBuffer(ebo);
            GL.BindVertexArray(0);
        }

        private void DrawHotbarBlockIcon(float x, float y, float w, float h, int texture)
        {
            // Draw a textured quad in NDC using the given texture
            float[] verts = {
        x,     y,     0, 0,
        x + w, y,     1, 0,
        x + w, y + h, 1, 1,
        x,     y + h, 0, 1
    };
            uint[] inds = { 0, 1, 2, 2, 3, 0 };
            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            int ebo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, inds.Length * sizeof(uint), inds, BufferUsageHint.DynamicDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
            // Use a simple shader for textured quad
            if (_hotbarTextShader == null)
                _hotbarTextShader = new Shader(hotbarTextVertexSource, hotbarTextFragmentSource);
            _hotbarTextShader.Use();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            GL.DeleteVertexArray(vao);
        }

        private void DrawHotbarCountText(int count, float x, float y, float w, float h)
        {
            // Render the count as a text to a bitmap, then upload as OpenGL texture and draw as quad
            string text = count.ToString();
            using (var bmp = new Bitmap(32, 32))
            using (var gfx = Graphics.FromImage(bmp))
            using (var font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(System.Drawing.Color.White))
            {
                gfx.Clear(System.Drawing.Color.Transparent);
                gfx.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
                gfx.DrawString(text, font, brush, 0, 0);
                // Flip the bitmap vertically before uploading to OpenGL
                bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);
                var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int tex = GL.GenTexture();
                GL.BindTexture(TextureTarget.Texture2D, tex);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, bmp.Width, bmp.Height, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, data.Scan0);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                bmp.UnlockBits(data);
                // Draw textured quad in NDC
                float[] verts = {
            x,     y,     0, 0,
            x + w, y,     1, 0,
            x + w, y + h, 1, 1,
            x,     y + h, 0, 1
        };
                uint[] inds = { 0, 1, 2, 2, 3, 0 };
                int vao = GL.GenVertexArray();
                int vbo = GL.GenBuffer();
                int ebo = GL.GenBuffer();
                GL.BindVertexArray(vao);
                GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
                GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float), verts, BufferUsageHint.DynamicDraw);
                GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
                GL.BufferData(BufferTarget.ElementArrayBuffer, inds.Length * sizeof(uint), inds, BufferUsageHint.DynamicDraw);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
                // Use a simple shader for textured quad
                if (_hotbarTextShader == null)
                    _hotbarTextShader = new Shader(hotbarTextVertexSource, hotbarTextFragmentSource);
                _hotbarTextShader.Use();
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, tex);
                GL.DrawElements(PrimitiveType.Triangles, 6, DrawElementsType.UnsignedInt, 0);
                GL.DeleteTexture(tex);
                GL.DeleteBuffer(vbo);
                GL.DeleteBuffer(ebo);
                GL.DeleteVertexArray(vao);
            }
        }

        // --- Chunk worker startup ---
        private void StartChunkWorkers()
        {
            for (int i = 0; i < _chunkWorkerCount; i++)
            {
                var t = new Thread(ChunkWorkerLoop) { IsBackground = true };
                _chunkWorkers.Add(t);
                t.Start();
            }
        }
        private void StopChunkWorkers()
        {
            _chunkWorkersRunning = false;
           
           

            foreach (var t in _chunkWorkers) t.Join();
        }

        // --- Request chunk load/generation (async) ---
        private void RequestChunkLoad(int chunkX, int chunkY, int chunkZ)
        {
            var key = (chunkX, chunkY, chunkZ);
            lock (_chunksLoading)
            {
                if (_chunks.TryGetValue(key, out var chunk))
                {
                    // If chunk is loaded, only enqueue if mesh is dirty
                    if (!chunk.MeshDirty || _chunksLoading.Contains(key) || chunkY < 0) return;
                }
                else
                {
                    if (_chunksLoading.Contains(key) || chunkY < 0) return;
                }
                _chunksLoading.Add(key);
                _chunkLoadQueue.Enqueue(key);
            }
        }

        // --- Chunk worker thread loop ---
        private void ChunkWorkerLoop()
        {
            while (_chunkWorkersRunning)
            {
                if (_chunkLoadQueue.TryDequeue(out var key))
                {
                    var (chunkX, chunkY, chunkZ) = key;
                    Chunk? chunk;
                    string chunkFile = $"chunk_{chunkX}_{chunkY}_{chunkZ}.bin";
                    string saveDir = Path.Combine("save", VoxelWindow.CurrentWorldName, "chunks");
                    string chunkPath = Path.Combine(saveDir, chunkFile);
                    lock (_chunksLock)
                    {
                        if (_chunks.TryGetValue(key, out chunk) && chunk != null)
                        {
                            // Already loaded, just rebuild mesh
                            chunk.RebuildMesh();
                            float[] meshData = chunk.Vertices.ToArray();
                            _meshIntegrationQueue.Enqueue((chunk, meshData));
                            lock (_chunksLoading) { _chunksLoading.Remove(key); }
                            continue;
                        }
                    }
                    if (File.Exists(chunkPath))
                    {
                        chunk = new Chunk(chunkX, chunkY, chunkZ, false);
                        chunk.GlobalIsBlockAt = GlobalIsBlockAt;
                        chunk.LoadFromFile(chunkFile);
                    }
                    else
                    {
                        chunk = new Chunk(chunkX, chunkY, chunkZ, true);
                        chunk.GlobalIsBlockAt = GlobalIsBlockAt;
                    }
                    // Build mesh (off main thread)
                    chunk.RebuildMesh();
                    float[] meshData2 = chunk.Vertices.ToArray();
                    _meshIntegrationQueue.Enqueue((chunk, meshData2));
                    lock (_chunksLock)
                    {
                        int[] dx = { 1, -1, 0, 0, 0, 0 };
                        int[] dy = { 0, 0, 1, -1, 0, 0 };
                        int[] dz = { 0, 0, 0, 0, 1, -1 };
                        for (int i = 0; i < 6; i++)
                        {
                            var nkey = (chunkX + dx[i], chunkY + dy[i], chunkZ + dz[i]);
                            if (_chunks.TryGetValue(nkey, out var neighbor))
                            {
                                neighbor.MeshDirty = true;
                                lock (_chunksLoading)
                                {
                                    if (!_chunksLoading.Contains(nkey))
                                    {
                                        _chunksLoading.Add(nkey);
                                        _chunkLoadQueue.Enqueue(nkey);
                                    }
                                }
                            }
                        }
                    }
                    lock (_chunksLoading) { _chunksLoading.Remove(key); }
                }
                else
                {
                    Thread.Sleep(2);
                }
            }
        }

        // --- Integrate finished chunk meshes on main thread ---
        private void IntegrateChunkMeshes()
        {
            while (_meshIntegrationQueue.TryDequeue(out var item))
            {
                var (chunk, meshData) = item;
                var key = (chunk.WorldX, chunk.WorldY, chunk.WorldZ);
                lock (_chunksLock)
                {
                    _chunks[key] = chunk;
                    _chunkMeshes[key] = meshData;
                }
                // --- VBO creation and upload (main thread only) ---
                if (chunk.VboHandle == 0)
                    chunk.VboHandle = GL.GenBuffer();
                if (chunk.VaoHandle == 0)
                    chunk.VaoHandle = GL.GenVertexArray();
                GL.BindVertexArray(chunk.VaoHandle);
                GL.BindBuffer(BufferTarget.ArrayBuffer, chunk.VboHandle);
                if (meshData.Length > 0)
                    GL.BufferData(BufferTarget.ArrayBuffer, meshData.Length * sizeof(float), meshData, BufferUsageHint.StaticDraw);
                chunk.VertexCount = meshData.Length / 9;
                // Set up vertex attributes ONCE per chunk VAO
                GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), 0);
                GL.EnableVertexAttribArray(0);
                GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 9 * sizeof(float), 3 * sizeof(float));
                GL.EnableVertexAttribArray(1);
                GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 9 * sizeof(float), 6 * sizeof(float));
                GL.EnableVertexAttribArray(2);
                GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, 9 * sizeof(float), 8 * sizeof(float));
                GL.EnableVertexAttribArray(3);
                GL.BindVertexArray(0);
                // --- end VBO/VAO upload ---
            }
        }

        // --- Refactored chunk helpers ---
        private (int, int, int) WorldToChunkCoords(int x, int y, int z)
        {
            int cx = (int)MathF.Floor(x / (float)ChunkSize);
            int cy = (int)MathF.Floor(y / (float)ChunkSize);
            int cz = (int)MathF.Floor(z / (float)ChunkSize);
            return (cx, cy, cz);
        }
        private bool GlobalIsBlockAt(int x, int y, int z)
        {
            var (cx, cy, cz) = WorldToChunkCoords(x, y, z);
            if (_chunks.TryGetValue((cx, cy, cz), out var chunk))
            {
                int lx = ((x % ChunkSize) + ChunkSize) % ChunkSize;
                int ly = ((y % ChunkSize) + ChunkSize) % ChunkSize;
                int lz = ((z % ChunkSize) + ChunkSize) % ChunkSize;
                return chunk.IsBlockAt(lx, ly, lz);
            }
            return false;
        }
       
       
        private bool TryGetChunkAndLocal(int x, int y, int z, out Chunk? chunk, out int lx, out int ly, out int lz)
        {
            var (cx, cy, cz) = WorldToChunkCoords(x, y, z);
            chunk = null;
            lx = ly = lz = 0;
            if (cy < 0) return false;
            if (_chunks.TryGetValue((cx, cy, cz), out var c))
            {
                chunk = c;
                lx = ((x % ChunkSize) + ChunkSize) % ChunkSize;
                ly = ((y % ChunkSize) + ChunkSize) % ChunkSize;
                lz = ((z % ChunkSize) + ChunkSize) % ChunkSize;
                return true;
            }
            return false;
        }
        private bool IsBlockAt(int x, int y, int z)
      
       {
            if (TryGetChunkAndLocal(x, y, z, out var chunk, out int lx, out int ly, out int lz) && chunk != null)
                return chunk.IsBlockAt(lx, ly, lz);
            return false;
        }
        private int GetBlockType(int x, int y, int z)
        {
            if (TryGetChunkAndLocal(x, y, z, out var chunk, out int lx, out int ly, out int lz) && chunk != null)
                return chunk.GetBlockType(lx, ly, lz);
            return 0;
        }
        private void PlaceBlock(int x, int y, int z, int blockType = 0)
        {
            if (TryGetChunkAndLocal(x, y, z, out var chunk, out int lx, out int ly, out int lz) && chunk != null)
            {
                chunk.PlaceBlock(lx, ly, lz, blockType);
                var (cx, cy, cz) = WorldToChunkCoords(x, y, z);
                RequestChunkLoad(cx, cy, cz);
                int[] dx = { 1, -1, 0, 0, 0, 0 };
                int[] dy = { 0, 0, 1, -1, 0, 0 };
                int[] dz = { 0, 0, 0, 0, 1, -1 };
                for (int i = 0; i < 6; i++)
                    RequestChunkLoad(cx + dx[i], cy + dy[i], cz + dz[i]);
            }
        }
        private void BreakBlock(int x, int y, int z)
        {
            if (TryGetChunkAndLocal(x, y, z, out var chunk, out int lx, out int ly, out int lz) && chunk != null)
            {
                chunk.BreakBlock(lx, ly, lz);
                var (cx, cy, cz) = WorldToChunkCoords(x, y, z);
                RequestChunkLoad(cx, cy, cz);
                int[] dx = { 1, -1, 0, 0, 0, 0 };
                int[] dy = { 0, 0, 1, -1, 0, 0 };
                int[] dz = { 0, 0, 0, 0, 1, -1 };
                for (int i = 0; i < 6; i++)
                    RequestChunkLoad(cx + dx[i], cy + dy[i], cz + dz[i]);
            }
        }
        // --- Refactored chunk generation to queue chunk loads ---
        private void GenerateInitialChunks(int radius)
        {
            int playerChunkX = (int)MathF.Floor(playerPos.X / ChunkSize);
            int playerChunkY = (int)MathF.Floor(playerPos.Y / ChunkSize);
            int playerChunkZ = (int)MathF.Floor(playerPos.Z / ChunkSize);
            int vertical = Settings.VerticalRenderDistance;
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -vertical; dy <= vertical; dy++)
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int cx = playerChunkX + dx;
                        int cy = playerChunkY + dy;
                        int cz = playerChunkZ + dz;
                        if (cy < 0) continue;
                        RequestChunkLoad(cx, cy, cz);
                    }
        }
        // --- Refactored dynamic chunk loading ---
        private void LoadNearbyChunks()
        {
            int radius = Settings.RenderDistance;
            int vertical = Settings.VerticalRenderDistance;
            int playerChunkX = (int)MathF.Floor(playerPos.X / ChunkSize);
            int playerChunkY = (int)MathF.Floor(playerPos.Y / ChunkSize);
            int playerChunkZ = (int)MathF.Floor(playerPos.Z / ChunkSize);
            for (int dx = -radius; dx <= radius; dx++)
                for (int dy = -vertical; dy <= vertical; dy++)
                    for (int dz = -radius; dz <= radius; dz++)
                    {
                        int cx = playerChunkX + dx;
                        int cy = playerChunkY + dy;
                        int cz = playerChunkZ + dz;
                        if (cy < 0) continue;
                        RequestChunkLoad(cx, cy, cz);
                    }
        }

        // Unload chunks that are far from the player to free memory
        private void UnloadFarChunks()
        {
            int radius = Settings.RenderDistance + 2; // Add a buffer to avoid rapid load/unload
            int vertical = Settings.VerticalRenderDistance + 2;
            int playerChunkX = (int)MathF.Floor(playerPos.X / ChunkSize);
            int playerChunkY = (int)MathF.Floor(playerPos.Y / ChunkSize);
            int playerChunkZ = (int)MathF.Floor(playerPos.Z / ChunkSize);
            var keysToRemove = new List<(int, int, int)>();
            lock (_chunksLock)
            {
                foreach (var key in _chunks.Keys)
                {
                    int dx = key.Item1 - playerChunkX;
                    int dy = key.Item2 - playerChunkY;
                    int dz = key.Item3 - playerChunkZ;
                    if (Math.Abs(dx) > radius || Math.Abs(dy) > vertical || Math.Abs(dz) > radius)
                    {
                        // Save chunk to disk before removing
                        string chunkFile = $"chunk_{key.Item1}_{key.Item2}_{key.Item3}.bin";
                        _chunks[key].SaveToFile(chunkFile);
                        keysToRemove.Add(key);
                    }
                }
                foreach (var key in keysToRemove)
                {
                    // Mark all 6 neighbors as MeshDirty and enqueue for remesh BEFORE removing the chunk
                    int[] dx = { 1, -1, 0, 0, 0, 0 };
                    int[] dy = { 0, 0, 1, -1, 0, 0 };
                    int[] dz = { 0, 0, 0, 0, 1, -1 };
                    for (int i = 0; i < 6; i++)
                    {
                        var nkey = (key.Item1 + dx[i], key.Item2 + dy[i], key.Item3 + dz[i]);
                        if (_chunks.TryGetValue(nkey, out var neighbor))
                        {
                            neighbor.MeshDirty = true;
                            RequestChunkLoad(nkey.Item1, nkey.Item2, nkey.Item3);
                        }
                    }
                    // Now remove the chunk and its mesh
                    _chunks.Remove(key);
                    _chunkMeshes.Remove(key);
                }
            }
        }

        private void save()
        {
            int playerChunkX = (int)MathF.Floor(playerPos.X / ChunkSize);
            int playerChunkY = (int)MathF.Floor(playerPos.Y / ChunkSize);
            int playerChunkZ = (int)MathF.Floor(playerPos.Z / ChunkSize);
            foreach (var key in _chunks.Keys)
            {
                int dx = key.Item1 - playerChunkX;
                int dy = key.Item2 - playerChunkY;
                int dz = key.Item3 - playerChunkZ;
                {
                    // Save chunk to disk before removing
                    string chunkFile = $"chunk_{key.Item1}_{key.Item2}_{key.Item3}.bin";
                    _chunks[key].SaveToFile(chunkFile);
                }
            }
            SavePlayerData(); // Save player position and inventory
        }

         private void SavePlayerData()
        {
            string saveDir = Path.Combine("save", CurrentWorldName);
            Directory.CreateDirectory(saveDir);
            string playerFile = Path.Combine(saveDir, "player.txt");
            using (var sw = new StreamWriter(playerFile))
            {
                // Save position
                sw.WriteLine($"{playerPos.X},{playerPos.Y},{playerPos.Z}");
                if (_camera != null) sw.WriteLine($"{_camera.Rotation.X},{_camera.Rotation.Y},{_camera.Rotation.Z}");
                // Save inventory (blockId:count per slot, separated by ;)
                for (int i = 0; i < _inventory.Items.Length; i++)
                {
                    var item = _inventory.Items[i];
                    sw.Write($"{item.BlockId}:{item.Count}");
                    if (i < _inventory.Items.Length - 1) sw.Write(";");
                }
                sw.WriteLine();
            }
        }

        private void LoadPlayerData()
        {
            string saveDir = Path.Combine("save", CurrentWorldName);
            string playerFile = Path.Combine(saveDir, "player.txt");
            if (!File.Exists(playerFile)) return;
            var lines = File.ReadAllLines(playerFile);
            if (lines.Length > 0)
            {
                var pos = lines[0].Split(',');
                if (pos.Length == 3 &&
                    float.TryParse(pos[0], out float x) &&
                    float.TryParse(pos[1], out float y) &&
                    float.TryParse(pos[2], out float z))
                {
                    playerPos = new Vector3(x, y, z);
                    if (_camera != null)
                    {
                        // Update camera position to match loaded player position
                        _camera.Position = playerPos - new Vector3(0.5f, 0.5f, 0.5f);
                        _camera.Centre = _camera.Position + new Vector3(0.5f, 0.5f, 0.5f);
                    }
                }
            }
            if (lines.Length > 1)
            {    
                var rot = lines[1].Split(',');
                if (rot.Length == 3 &&
                    float.TryParse(rot[0], out float xr) &&
                    float.TryParse(rot[1], out float yr) &&
                    float.TryParse(rot[2], out float zr))
                {
                    if (_camera != null)
                        _camera._rotation = new Vector3(xr, yr, zr);
                }
            }
            // Clear inventory before loading
            for (int i = 0; i < _inventory.Items.Length; i++)
            {
                _inventory.Items[i].BlockId = 0;
                _inventory.Items[i].Count = 0;
            }
            if (lines.Length > 2)
            {
                var slots = lines[2].Split(';');
                for (int i = 0; i < Math.Min(slots.Length, _inventory.Items.Length); i++)
                {
                    var parts = slots[i].Split(':');
                    if (parts.Length == 2 &&
                        int.TryParse(parts[0], out int blockId) &&
                        int.TryParse(parts[1], out int count))
                    {
                        _inventory.Items[i].BlockId = blockId;
                        _inventory.Items[i].Count = count;
                    }
                }
            }
        }

    }

    public class Chunk
    {
        private const int Width = 16;
        private const int Height = 16;
        private const int Depth = 16;
        private int[,,] blockTypes = new int[Width, Height, Depth];
        public List<float> Vertices { get; private set; } = new();
        public Func<int, int, int, bool>? GlobalIsBlockAt;
        public int WorldX, WorldY, WorldZ;
        public bool MeshDirty = true;
        // --- VAO and VBO for this chunk ---
        public int VaoHandle = 0; // Add VAO handle for this chunk
        public int VboHandle = 0;
        public int VertexCount = 0;

        // Only generate terrain if requested (default true)
        public Chunk(int worldX, int worldY, int worldZ, bool generateTerrain = true)
        {
            WorldX = worldX;
            WorldY = worldY;
            WorldZ = worldZ;
            if (generateTerrain)
            {
                for (int x = 0; x < Width; x++)
                    for (int z = 0; z < Depth; z++)
                        for (int y = 0; y < Height; y++)
                        {
                            int globalY = y + worldY * Height;
                            // Simple terrain: grass on top, dirt below, air above
                            if (globalY == 100)
                                blockTypes[x, y, z] = 2; // grass
                            else if (globalY < 100)
                                blockTypes[x, y, z] = 1; // dirt
                            else
                                blockTypes[x, y, z] = 0; // air
                        }
            }
            // Always create VBO for this chunk
            if (VboHandle == 0)
                VboHandle = GL.GenBuffer();
        }

        public bool IsBlockAt(int x, int y, int z)
        {
            // Out of bounds means no block (air)
            if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                return false;
            return blockTypes[x, y, z] != 0; // 0 is air
        }

        public int GetBlockType(int x, int y, int z)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                return 0;
            return blockTypes[x, y, z];
        }

        private void AddCube(Vector3 pos)
        {
            int x = (int)pos.X;
            int y = (int)pos.Y;
            int z = (int)pos.Z;
            int blockType = blockTypes[x, y, z];
            // Use global block query for face culling
            if (GlobalIsBlockAt == null) return;
            int wx = WorldX * Width + x;
            int wy = WorldY * Height + y;
            int wz = WorldZ * Depth + z;
            // Only add a face if the neighbor in that direction is air (blockType == 0)
            if (!GlobalIsBlockAt(wx + 1, wy, wz)) AddFace(pos, Vector3.UnitX, blockType);
            if (!GlobalIsBlockAt(wx - 1, wy, wz)) AddFace(pos, -Vector3.UnitX, blockType);
            if (!GlobalIsBlockAt(wx, wy + 1, wz)) AddFace(pos, Vector3.UnitY, blockType);
            if (!GlobalIsBlockAt(wx, wy - 1, wz)) AddFace(pos, -Vector3.UnitY, blockType);
            if (!GlobalIsBlockAt(wx, wy, wz + 1)) AddFace(pos, Vector3.UnitZ, blockType);
            if (!GlobalIsBlockAt(wx, wy, wz - 1)) AddFace(pos, -Vector3.UnitZ, blockType);
        }

        private void AddFace(Vector3 blockPos, Vector3 normal, int blockType)
        {
            Vector3 up = (normal == Vector3.UnitY || normal == -Vector3.UnitY) ? Vector3.UnitZ : Vector3.UnitY;
            Vector3 right = Vector3.Cross(normal, up);
            Vector3 faceCenter = blockPos + (normal * 0.5f);
            Vector3 v0 = faceCenter - right * 0.5f - up * 0.5f;
            Vector3 v1 = faceCenter + right * 0.5f - up * 0.5f;
            Vector3 v2 = faceCenter + right * 0.5f + up * 0.5f;
            Vector3 v3 = faceCenter - right * 0.5f + up * 0.5f;
            Vector3 color = new Vector3(1.0f, 1.0f, 1.0f);
            AddQuad(v0, v1, v2, v3, color, blockType);
        }

        private void AddQuad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 color, int blockType)
        {
            AddVertex(v0, color, new Vector2(0, 0), blockType);
            AddVertex(v1, color, new Vector2(1, 0), blockType);
            AddVertex(v2, color, new Vector2(1, 1), blockType);
            AddVertex(v2, color, new Vector2(1, 1), blockType);
            AddVertex(v3, color, new Vector2(0, 1), blockType);
            AddVertex(v0, color, new Vector2(0, 0), blockType);
        }

        private void AddVertex(Vector3 position, Vector3 color, Vector2 uv, int blockType)
        {
            Vertices.Add(position.X);
            Vertices.Add(position.Y);
            Vertices.Add(position.Z);
            Vertices.Add(color.X);
            Vertices.Add(color.Y);
            Vertices.Add(color.Z);
            Vertices.Add(uv.X);
            Vertices.Add(uv.Y);
            Vertices.Add((float)blockType); // block type as float
        }

        public void PlaceBlock(int x, int y, int z, int blockType = 0)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                return;
            blockTypes[x, y, z] = blockType;
            MeshDirty = true;
            // RebuildMesh(); // Remove direct call, let worker handle
        }

        public void BreakBlock(int x, int y, int z)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height || z < 0 || z >= Depth)
                return;
            blockTypes[x, y, z] = 0; // air (id 0)
            MeshDirty = true;
            // RebuildMesh(); // Remove direct call, let worker handle
        }

        public void RebuildMesh()
        {
            Vertices.Clear();
            int nonAirBlocks = 0;
            int facesAdded = 0;
            for (int x = 0; x < Width; x++)
                for (int y = 0; y < Height; y++)
                    for (int z = 0; z < Depth; z++)
                        if (blockTypes[x, y, z] != 0) // 0 is air
                        {
                            nonAirBlocks++;
                            int before = Vertices.Count;
                            AddCube(new Vector3(x, y, z));
                            facesAdded += (Vertices.Count - before) / (6 * 9); // 6 vertices per face, 9 floats per vertex
                        }
            MeshDirty = false;
            VertexCount = Vertices.Count / 9;
            // VBO creation and upload moved to main thread!
            
            
            
        }

        public void Dispose()
        {
            if (VboHandle != 0)
            {
                GL.DeleteBuffer(VboHandle);
                VboHandle = 0;
            }
        }

        private static readonly object _chunkFileLock = new();

        // Update SaveToFile/LoadFromFile to use 3D chunk coordinates in filename
        public void SaveToFile(string path)
        {

            lock (_chunkFileLock)
            {
                string saveDir = Path.Combine("save", VoxelWindow.CurrentWorldName, "chunks");
                Directory.CreateDirectory(saveDir);
                string fileName = Path.GetFileName(path);
                string chunkPath = Path.Combine(saveDir, fileName);
                using (var fs = new FileStream(chunkPath, FileMode.Create, FileAccess.Write))
                using (var bw = new BinaryWriter(fs))
                {
                    for (int x = 0; x < Width; x++)
                        for (int y = 0; y < Height; y++)
                            for (int z = 0; z < Depth; z++)
                                bw.Write(blockTypes[x, y, z]);
                }
            }
        }

        public void LoadFromFile(string path)
        {
            lock (_chunkFileLock)
            {
                string saveDir = Path.Combine("save", VoxelWindow.CurrentWorldName, "chunks");
                string fileName = Path.GetFileName(path);
                string chunkPath = Path.Combine(saveDir, fileName);
                if (!File.Exists(chunkPath)) return;
                int expectedInts = Width * Height * Depth;
                int expectedBytes = expectedInts * sizeof(int);
                var fileInfo = new FileInfo(chunkPath);
                if (fileInfo.Length < expectedBytes)
                {
                    // File is too short/corrupt, skip loading and regenerate
                    return;
                }
                try
                {
                    using (var fs = new FileStream(chunkPath, FileMode.Open, FileAccess.Read))
                    using (var br = new BinaryReader(fs))
                    {
                        for (int x = 0; x < Width; x++)
                            for (int y = 0; y < Height; y++)
                                for (int z = 0; z < Depth; z++)
                                    blockTypes[x, y, z] = br.ReadInt32();
                    }
                }
                catch (EndOfStreamException)
                {
                    // File is corrupt, skip loading and regenerate
                    return;
                }
                catch (Exception)
                {
                    return;
                }
                RebuildMesh();
            }
        }
    }

    // Inventory item struct
    public struct InventoryItem
    {
        public int BlockId; // For now, 0 = grass, can expand later
        public int Count;
        public InventoryItem(int blockId, int count)
        {
            BlockId = blockId;
            Count = count;
        }
    }

    // Inventory class
    public class Inventory
    {
        public const int HotbarSize = 9;
        public const int InventorySize = 27;
        public const int MaxStackSize = 64;
        public const int MaxTotalItems = (HotbarSize + InventorySize) * MaxStackSize;
        public InventoryItem[] Items = new InventoryItem[InventorySize];
        public int SelectedHotbarSlot = 0;

        // Block IDs
        public const int BlockGrass = 0;
        public const int BlockDirt = 1;

        public Inventory()
        {
            // Use BlockRegistry for block IDs
            Items[0] = new InventoryItem(1, 27); // 27 dirt blocks (id 1)
            Items[1] = new InventoryItem(2, 27); // 27 grass blocks (id 2)
            for (int i = 2; i < HotbarSize; i++)
                Items[i] = new InventoryItem(0, 0); // air/empty
        }

        public InventoryItem GetSelectedItem()
        {
            return Items[SelectedHotbarSlot];
        }

        public void AddBlock(int blockId, int count = 1)
        {
            // Count total items in inventory
            int total = 0;
            for (int i = 0; i < InventorySize; i++)
                total += Items[i].Count;
            int canAdd = Math.Max(0, MaxTotalItems - total);
            if (canAdd == 0) return; // Inventory full
            int toAdd = Math.Min(count, canAdd);
            // Merge all of this block type into a single stack
            int stackIndex = -1;
            for (int i = 0; i < InventorySize; i++)
            {
                if (Items[i].BlockId == blockId && Items[i].Count > 0)
                {
                    stackIndex = i;
                    break;
                }
            }
            if (stackIndex != -1)
            {
                Items[stackIndex].Count += toAdd;
            }
            else
            {
                // Find empty slot
                for (int i = 0; i < InventorySize; i++)
                {
                    if (Items[i].Count == 0)
                    {
                        Items[i] = new InventoryItem(blockId, toAdd);
                        break;
                    }
                }
            }
            // Remove any duplicate stacks of the same type (merge them)
            int mergedCount = 0;
            int firstIndex = -1;
            for (int i = 0; i < InventorySize; i++)
            {
                if (Items[i].BlockId == blockId && Items[i].Count > 0)
                {
                    if (firstIndex == -1)
                    {
                        firstIndex = i;
                        mergedCount = Items[i].Count;
                    }
                    else
                    {
                        mergedCount += Items[i].Count;
                        Items[i] = new InventoryItem(0, 0);
                    }
                }
            }
            if (firstIndex != -1)
                Items[firstIndex].Count = mergedCount;
        }

        public bool RemoveBlock(int blockId, int count = 1)
        {
            for (int i = 0; i < InventorySize; i++)
            {
                if (Items[i].BlockId == blockId && Items[i].Count >= count)

                {
                    Items[i].Count -= count;
                    if (Items[i].Count <= 0)
                        Items[i] = new InventoryItem(0, 0);
                    return true;
                }
            }
            return false;
        }
    }

    public class Shader
    {
        public int Handle;

        public Shader(string vertexSource, string fragmentSource)
        {
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompile(vertexShader);

            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompile(fragmentShader);

            Handle = GL.CreateProgram();
            GL.AttachShader(Handle, vertexShader);
            GL.AttachShader(Handle, fragmentShader);
            GL.LinkProgram(Handle);
            CheckProgramLink(Handle);

            GL.DetachShader(Handle, vertexShader);
            GL.DetachShader(Handle, fragmentShader);
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        private void CheckShaderCompile(int shader)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetShaderInfoLog(shader);
                throw new Exception($"Shader compilation failed: {infoLog}");
            }
        }

        private void CheckProgramLink(int program)
        {
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int success);
            if (success == 0)
            {
                string infoLog = GL.GetProgramInfoLog(program);
                throw new Exception($"Program linking failed: {infoLog}");
            }
        }

        public void Use() => GL.UseProgram(Handle);
    }

    public class Camera
    {
        public Vector3 Centre;
        public Vector3 Position;
        private float _aspectRatio;
        public float AspectRatio
        {
            get => _aspectRatio;
            set => _aspectRatio = value;
        }
        public Vector3 _rotation = -Vector3.UnitZ;
        private Vector3 _front = -Vector3.UnitZ;
        private Vector3 _up = Vector3.UnitY;
        private Vector3 _right = Vector3.UnitX;

        public float Pitch { get; set; } = 0f;
        public float Yaw { get; set; } = -90f; // Facing towards negative Z initially

        public Camera(Vector3 position, float aspectRatio)
        {
            Position = position;
            _aspectRatio = aspectRatio;
            UpdateVectors();
        }

        public Vector3 Rotation => _rotation;
        public Vector3 Front => _front;
        public Vector3 Up => _up;
        public Vector3 Right => _right;

        public Matrix4 GetViewMatrix()
        {
            return Matrix4.LookAt(Position, Position + _front, _up);
        }

        public Matrix4 GetProjectionMatrix()
        {
            return Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(70f), _aspectRatio, 0.001f, 1000f);
        }

        public void UpdateVectors()
        {
            Vector3 front;
            front.X = MathF.Cos(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch));
            front.Y = MathF.Sin(MathHelper.DegreesToRadians(Pitch));
            front.Z = MathF.Sin(MathHelper.DegreesToRadians(Yaw)) * MathF.Cos(MathHelper.DegreesToRadians(Pitch));
            _front = Vector3.Normalize(front);
            Vector3 rotation;
            rotation.X = MathF.Cos(MathHelper.DegreesToRadians(Yaw));
            rotation.Y = MathF.Sin(MathHelper.DegreesToRadians(Pitch));
            rotation.Z = MathF.Sin(MathHelper.DegreesToRadians(Yaw));
            _rotation = Vector3.Normalize(rotation);
            _right = Vector3.Normalize(Vector3.Cross(_front, Vector3.UnitY));
            _up = Vector3.Normalize(Vector3.Cross(_right, _front));
        }
    }

    public static class Program
    {
        public static void Main()
        {
            var gameWindowSettings = new GameWindowSettings
            {
                UpdateFrequency = 60.0
            };

            var nativeWindowSettings = new NativeWindowSettings
            {
                ClientSize = new Vector2i(1280, 720),
                Title = "Voxel Demo with Camera Movement"
            };

            using var window = new VoxelWindow(gameWindowSettings, nativeWindowSettings);
            window.Run();

        }

    }

    public class BlockDefinition
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string texture { get; set; } = "";
    }

    public static class BlockRegistry
    {
        private static Dictionary<int, BlockDefinition> _blocks = new();
        public static IReadOnlyDictionary<int, BlockDefinition> Blocks => _blocks;

        public static void Load(string path)
        {
            var json = File.ReadAllText(path);
            var blocks = JsonSerializer.Deserialize<List<BlockDefinition>>(json);
            _blocks = blocks?.ToDictionary(b => b.id) ?? new();
        }

        public static BlockDefinition? Get(int id)
        {
            _blocks.TryGetValue(id, out var def);
            return def;
        }
        
    }
}