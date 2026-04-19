using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace RockEngine.ShaderSyntax
{
    internal class ShaderVersionStatusBar : IVsRunningDocTableEvents3, IDisposable
    {
        private readonly IServiceProvider _serviceProvider;
        private uint _cookie;
        private IVsRunningDocumentTable _rdt;
        private IVsStatusbar _statusBar;
        private bool _disposed;

        public ShaderVersionStatusBar(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _serviceProvider = serviceProvider;
            _rdt = _serviceProvider.GetService(typeof(SVsRunningDocumentTable)) as IVsRunningDocumentTable;
            _statusBar = _serviceProvider.GetService(typeof(SVsStatusbar)) as IVsStatusbar;

            if (_rdt != null)
            {
                _rdt.AdviseRunningDocTableEvents(this, out _cookie);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_rdt != null && _cookie != 0)
                {
                    _rdt.UnadviseRunningDocTableEvents(_cookie);
                    _cookie = 0;
                }
                _disposed = true;
            }
        }

        #region IVsRunningDocTableEvents3 implementation

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            UpdateForDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            // Known values: RDTA_DocDataReloaded = 0x4, RDTA_DocDataChanged = 0x8
            const uint RDTA_DocDataReloaded = 0x4;
            const uint RDTA_DocDataChanged = 0x8;
            if ((grfAttribs & RDTA_DocDataReloaded) != 0 || (grfAttribs & RDTA_DocDataChanged) != 0)
            {
                UpdateForDocument(docCookie);
            }
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            UpdateForDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterAttributeChangeEx(uint docCookie, uint grfAttribs, IVsHierarchy pHierOld, uint itemidOld, string pszMkDocumentOld, IVsHierarchy pHierNew, uint itemidNew, string pszMkDocumentNew)
        {
            // Document may have been renamed or moved; update
            UpdateForDocument(docCookie);
            return VSConstants.S_OK;
        }

        public int OnBeforeSave(uint docCookie)
        {
            return VSConstants.S_OK;
        }

        #endregion

        private void UpdateForDocument(uint docCookie)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_rdt == null || _statusBar == null)
            {
                return;
            }

            _rdt.GetDocumentInfo(docCookie, out _, out _, out _, out string moniker, out _, out _, out _);
            if (string.IsNullOrEmpty(moniker))
            {
                return;
            }

            // Only process shader files
            string ext = Path.GetExtension(moniker).ToLowerInvariant();
            if (!(ext == ".vert" || ext == ".frag" || ext == ".glsl" || ext == ".comp" || ext == ".geom"))
            {
                return;
            }

            try
            {
                string content = File.ReadAllText(moniker);
                string version = GetGlslVersion(content);
                var materialTextures = ParseMaterialBlocks(content);

                string statusText = $"GLSL {version}";
                if (materialTextures.Count > 0)
                {
                    statusText += $" | [MATERIAL] textures: {string.Join(", ", materialTextures)}";
                }

                _statusBar.SetText(statusText);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status bar: {ex}");
            }
        }

        private string GetGlslVersion(string text)
        {
            var match = Regex.Match(text, @"^\s*#version\s+(\d+)(?:\s+(\w+))?");
            if (match.Success)
            {
                string version = match.Groups[1].Value;
                string profile = match.Groups[2].Value;
                return string.IsNullOrEmpty(profile) ? version : $"{version} {profile}";
            }
            return "unknown";
        }

        private List<string> ParseMaterialBlocks(string text)
        {
            var names = new List<string>();
            var materialRegex = new Regex(@"\[MATERIAL\]\s*\{([^}]*)\}", RegexOptions.Singleline);
            var matches = materialRegex.Matches(text);
            foreach (Match match in matches)
            {
                string block = match.Groups[1].Value;
                var lines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim().TrimEnd(',', ';');
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        continue;
                    }

                    var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        names.Add(parts[1]);
                    }
                }
            }
            return names;
        }
    }
}