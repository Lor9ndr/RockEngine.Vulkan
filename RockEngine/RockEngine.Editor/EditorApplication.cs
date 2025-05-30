﻿using RockEngine.Core;
using RockEngine.Editor.Layers;

using Silk.NET.SDL;

namespace RockEngine.Editor
{
    public class EditorApplication : Application
    {
        public EditorApplication(int width, int height)
            : base("RockEngine", width, height)
        {
            OnLoad += Load;
        }

        private async Task Load()
        {
           // await PushLayer(new TitleBarLayer(_window, _inputContext));
            await PushLayer(new EditorLayer(_world, _context, _graphicsEngine, _renderer, _inputContext, _textureStreamer));
        }
    }
}
