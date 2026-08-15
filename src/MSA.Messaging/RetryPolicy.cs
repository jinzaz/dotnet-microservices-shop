using System;
using System.Threading.Tasks;

namespace MSA.Libraries.Messaging
{
    public static class RetryPolicy
    {
        public static async Task ExecuteAsync(Func<Task> action, int maxAttempts = 3, TimeSpan? delay = null, Action<Exception, int>? onRetry = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            var attempts = 0;
            var wait = delay ?? TimeSpan.FromSeconds(1);

            while (true)
            {
                try
                {
                    attempts++;
                    await action();
                    return;
                }
                catch (Exception ex)
                {
                    if (attempts >= maxAttempts) throw;
                    onRetry?.Invoke(ex, attempts);
                    await Task.Delay(wait);
                    wait = TimeSpan.FromTicks(wait.Ticks * 2); // exponential backoff
                }
            }
        }
    }
}
