namespace BoplEight.Lobby
{
    internal sealed class PendingLobbyJoinState
    {
        private ulong lobbyId;
        private float deadline;

        internal bool IsActive
        {
            get { return lobbyId != 0; }
        }

        internal void Begin(ulong newLobbyId, float now, float timeoutSeconds)
        {
            lobbyId = newLobbyId;
            deadline = now + timeoutSeconds;
        }

        internal bool TryComplete(ulong completedLobbyId)
        {
            if (lobbyId == 0 || completedLobbyId != lobbyId)
            {
                return false;
            }

            TryCancel();
            return true;
        }

        internal bool TryTakeTimeout(float now)
        {
            if (lobbyId == 0 || now < deadline)
            {
                return false;
            }

            TryCancel();
            return true;
        }

        internal bool TryCancel()
        {
            if (lobbyId == 0)
            {
                return false;
            }

            Clear();
            return true;
        }

        private void Clear()
        {
            lobbyId = 0;
            deadline = 0f;
        }
    }
}
