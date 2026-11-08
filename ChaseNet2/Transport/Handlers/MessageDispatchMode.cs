namespace ChaseNet2.Transport
{
    /// <summary>
    /// Selects the thread a message handler runs on.
    /// </summary>
    public enum MessageDispatchMode
    {
        /// <summary>
        /// The message is queued and the handler runs on the thread that calls
        /// <see cref="ConnectionManager.Dispatch"/> (or <see cref="ConnectionManager.Update"/>).
        /// Use this for application code that touches game or UI state.
        /// </summary>
        Queued,

        /// <summary>
        /// The handler runs on the network thread as soon as the message is decoded. It must be thread-safe and fast,
        /// and must not block. A message that arrived before an inline handler was registered on its channel is
        /// delivered to that handler from the dispatching thread instead.
        /// </summary>
        Inline,
    }
}
