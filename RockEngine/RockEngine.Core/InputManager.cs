using Silk.NET.Input;

namespace RockEngine.Core
{
    public class InputManager
    {
        public IInputContext Context { get; set; }
        public IMouse PrimaryMouse => Context.Mice[0];
        public IKeyboard PrimaryKeyboard => Context.Keyboards[0];
    }
}
