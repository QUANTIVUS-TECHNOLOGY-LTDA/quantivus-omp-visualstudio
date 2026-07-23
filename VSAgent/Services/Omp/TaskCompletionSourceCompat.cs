using System.Threading;
using System.Threading.Tasks;

namespace VSAgent.Services.Omp
{
    internal static class TaskCompletionSourceCompat
    {
        public static bool TrySetCanceled<T>(this TaskCompletionSource<T> source, CancellationToken cancellationToken)
        {
            return source.TrySetCanceled();
        }
    }
}
