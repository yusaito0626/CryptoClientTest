namespace Enums
{    
    public enum logType
    {
        NONE = 0,
        INFO = 1,
        WARNING = 2,
        ERROR = 3,
        FATAL = 4
    }

    public enum skewType
    {
        NONE = 0,
        LINEAR = 1,
        STEP = 2
    }

    public enum orderType
    {
        NONE = -1,
        Limit = 1,
        LimitMaker = 2,
        Market = 3,
        Other = 4
    }
    public enum orderAction
    {
        NONE = -1,
        New = 1,
        Mod = 2,
        Can = 3
    }
    public enum orderSide
    {
        NONE = -1,
        Buy = 1,
        Sell = 2
    }
    public enum orderStatus
    {
        NONE = -1,
        WaitOpen = 1,
        Open = 2,
        PartialFill = 3,
        WaitCancel = 4,
        Filled = 5,
        Canceled = 6,
        WaitMod = 98,
        INVALID = 99
    }
    public enum timeInForce
    {
        NONE = -1,
        GoodTillCanceled = 1,
        ImmediateOrCancel = 2,
        FillOrKill = 3
    }
    public enum fillType
    {
        NONE = -1,
        onFill = 1,
        onOrderUpdate = 2,
        onTrade = 3,
        onQuotes = 4
    }
    public enum  ordError
    {
        NONE = 0,
        RATE_LIMIT_EXCEEDED = 10009,
        TIMED_OUT = 80001,
        HTTP_NOT_READY = 80002,
        NONCE_ERROR = 90001
    }
    public enum msgType
    {
        NONE = 0,
        NOTIFICATION = 1,
        ERROR = 2,
        TEST = 3
    }
}
