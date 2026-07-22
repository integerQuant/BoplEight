namespace BoplEight.Lobby
{
    internal sealed class LobbyJoinGate
    {
        private int active;

        internal bool IsActive
        {
            get { return System.Threading.Volatile.Read(ref active) != 0; }
        }

        internal bool TryEnter()
        {
            return System.Threading.Interlocked.CompareExchange(ref active, 1, 0) == 0;
        }

        internal void Exit()
        {
            System.Threading.Interlocked.Exchange(ref active, 0);
        }
    }
}
