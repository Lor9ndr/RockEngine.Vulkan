using Silk.NET.GLFW;
using Silk.NET.Input;
using Silk.NET.Windowing;

namespace RockEngine.Core
{
    public class InputManager
    {
        public IWindow InputOwner { get; private set;}
        public IInputContext Context { get; private set; }
        public IMouse PrimaryMouse => Context.Mice[0];
        public IKeyboard PrimaryKeyboard => Context.Keyboards[0];

        public event Action<IInputContext, IInputContext>? OnInputActionChanged;

        public void SetInput(IWindow inputOwner, IInputContext inputContext)
        {
            var tmp = Context;
            InputOwner = inputOwner;
            Context = inputContext;
            OnInputActionChanged?.Invoke(tmp, Context);

        }
    }
}
