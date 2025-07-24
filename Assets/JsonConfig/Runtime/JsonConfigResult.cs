
namespace work.ctrl3d
{
    /// <summary>
    /// JSON 설정 작업 결과를 담는 클래스
    /// </summary>
    /// <typeparam name="T">설정 데이터 타입</typeparam>
    public class JsonConfigResult<T>
    {
        public bool IsSuccess { get; private set; }
        public T Data { get; private set; }
        public string ErrorMessage { get; private set; }
        public JsonConfigError Error { get; private set; }

        private JsonConfigResult(bool isSuccess, T data, string errorMessage, JsonConfigError error)
        {
            IsSuccess = isSuccess;
            Data = data;
            ErrorMessage = errorMessage;
            Error = error;
        }

        /// <summary>
        /// 성공 결과 생성
        /// </summary>
        /// <param name="data">성공 데이터</param>
        /// <returns>성공 결과</returns>
        public static JsonConfigResult<T> Success(T data)
        {
            return new JsonConfigResult<T>(true, data, null, JsonConfigError.None);
        }

        /// <summary>
        /// 실패 결과 생성
        /// </summary>
        /// <param name="errorMessage">에러 메시지</param>
        /// <param name="error">에러 타입</param>
        /// <returns>실패 결과</returns>
        public static JsonConfigResult<T> Failure(string errorMessage, JsonConfigError error)
        {
            return new JsonConfigResult<T>(false, default(T), errorMessage, error);
        }

        /// <summary>
        /// 암시적 bool 변환 (IsSuccess 반환)
        /// </summary>
        /// <param name="result">결과 객체</param>
        /// <returns>성공 여부</returns>
        public static implicit operator bool(JsonConfigResult<T> result)
        {
            return result?.IsSuccess ?? false;
        }

        /// <summary>
        /// 결과 정보를 문자열로 반환
        /// </summary>
        /// <returns>결과 문자열</returns>
        public override string ToString()
        {
            return IsSuccess ? $"Success: {Data}" : $"Failure: {ErrorMessage} (Error: {Error})";
        }
    }
}