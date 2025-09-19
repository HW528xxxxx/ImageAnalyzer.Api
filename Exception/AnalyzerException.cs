using System;

namespace ComputerVision.Exceptions
{
    public class AnalyzerException : Exception
    {
        public MessageCodeEnum Code { get; }

        public AnalyzerException(MessageCodeEnum code, string message) 
            : base(message)
        {
            Code = code;
        }

        public AnalyzerException(MessageCodeEnum code, string message, Exception innerException) 
            : base(message, innerException)
        {
            Code = code;
        }
    }
}
