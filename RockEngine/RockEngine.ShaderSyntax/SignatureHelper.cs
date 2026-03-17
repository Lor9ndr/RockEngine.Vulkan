using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

namespace RockEngine.ShaderSyntax
{
    internal class SignatureHelper : ISignature
    {
        private readonly List<IParameter> _parameters;
        private IParameter _currentParameter;

        public ITrackingSpan ApplicableToSpan { get; set; }
        public string Content { get; set; }
        public string PrettyPrintedContent { get; set; }
        public string Documentation { get; set; }

        ReadOnlyCollection<IParameter> ISignature.Parameters => new ReadOnlyCollection<IParameter>(_parameters);

        public IParameter CurrentParameter
        {
            get => _currentParameter;
            set
            {
                if (_currentParameter != value)
                {
                    var oldParameter = _currentParameter;
                    _currentParameter = value;
                    OnCurrentParameterChanged(new CurrentParameterChangedEventArgs(oldParameter, value));
                }
            }
        }

        public event EventHandler<CurrentParameterChangedEventArgs> CurrentParameterChanged;

        public SignatureHelper(string name, string parameters, string documentation, ITrackingSpan applicableToSpan)
        {
            Content = $"{name}({parameters})";
            PrettyPrintedContent = Content;
            Documentation = documentation;
            ApplicableToSpan = applicableToSpan;
            _parameters = new List<IParameter>();

            if (!string.IsNullOrEmpty(parameters))
            {
                int paramStart = Content.IndexOf('(') + 1;
                Span locus = new Span(paramStart, parameters.Length);
                var param = new ParameterHelper(this, "uv", parameters, "Texture coordinates", locus);
                _parameters.Add(param);
            }

            ComputeCurrentParameter();
        }

        public void ComputeCurrentParameter()
        {
            if (_parameters.Count == 0)
            {
                CurrentParameter = null;
                return;
            }
            CurrentParameter = _parameters[0];
        }

        protected virtual void OnCurrentParameterChanged(CurrentParameterChangedEventArgs e)
        {
            CurrentParameterChanged?.Invoke(this, e);
        }

        public ReadOnlyCollection<IParameter> Parameters => ((ISignature)this).Parameters;
    }
}