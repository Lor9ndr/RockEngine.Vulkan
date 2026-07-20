using System.Text.RegularExpressions;

namespace RockEngine.ShaderPreProcessor
{
    /// <summary>
    /// Data extracted from one annotation block.
    /// </summary>
    public class AnnotationBlockData
    {
        public string AnnotationName { get; set; }
        public string BlockContent { get; set;  }     // the text inside { ... }
        public Match RegexMatch { get; set; }        // the full regex match for source manipulation
        public int StartIndex { get;  set;}          // index in the full source
        public int EndIndex { get; set;}
    }
}