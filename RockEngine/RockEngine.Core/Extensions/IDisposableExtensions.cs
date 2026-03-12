namespace RockEngine.Core.Extensions
{
    internal static class IDisposableExtensions
    {
        extension(IDisposable disposable)
        {
            /// <summary>
            /// Merges two disposable into one
            /// </summary>
            /// <param name="left">left disposable</param>
            /// <param name="right">right disposable</param>
            /// <returns>merged disposable where first disposed will be left and the last disposable will be right</returns>
            public static IDisposable operator |(IDisposable left, IDisposable right)
            {
                return new MergedDisposable(left,right);
            }
        }
        public struct MergedDisposable(IDisposable left, IDisposable right) : IDisposable
        {
            public void Dispose()
            {
                left.Dispose();
                right.Dispose();
            }
        }
    }
}
