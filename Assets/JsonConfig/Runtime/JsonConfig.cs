using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
#if USE_UNITASK
using Cysharp.Threading.Tasks;
using System.Threading;
#endif

namespace work.ctrl3d
{
    /// <summary>
    /// 범용 JSON 설정 관리 클래스
    /// </summary>
    /// <typeparam name="T">설정 데이터 타입</typeparam>
    public class JsonConfig<T> where T : class, new()
    {
        private T _config;
        private readonly string _filePath;
        private readonly object _lockObject = new();

#if USE_UNITASK
        private readonly SemaphoreSlim _semaphore = new(1, 1);
#endif

        public event Action<T> ConfigChanged;
        private Func<T, bool> _validator;

        /// <summary>
        /// JsonConfig 생성자
        /// </summary>
        /// <param name="filePath">전체 파일 경로</param>
        public JsonConfig(string filePath)
        {
            _filePath = filePath;
        }

#if USE_UNITASK
        // --------------------------------------------------------------------------------
        // 비동기 메서드 (UniTask)
        // --------------------------------------------------------------------------------

        /// <summary>
        /// 설정 파일 비동기 로드
        /// </summary>
        public async UniTask<JsonConfigResult<T>> LoadAsync(bool createIfNotExists = true,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await LoadInternalAsync(createIfNotExists, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 로드된 설정 데이터에서 특정 섹션을 비동기적으로 가져옵니다.
        /// </summary>
        public async UniTask<TSection> GetSectionAsync<TSection>(string sectionName,
            CancellationToken cancellationToken = default) where TSection : class
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                if (_config == null)
                {
                    var loadResult = await LoadInternalAsync(true, cancellationToken);
                    if (!loadResult.IsSuccess)
                    {
                        Debug.LogError("GetSectionAsync를 위해 설정을 로드하는 데 실패했습니다.");
                        return null;
                    }
                }

                var jObject = JObject.FromObject(_config);
                var sectionToken = jObject[sectionName];

                if (sectionToken == null)
                {
                    Debug.LogWarning($"섹션 '{sectionName}'을(를) 찾을 수 없습니다.");
                    return null;
                }

                return sectionToken.ToObject<TSection>();
            }
            catch (Exception ex)
            {
                Debug.LogError($"섹션 '{sectionName}'을(를) '{typeof(TSection).Name}' 타입으로 변환하는 데 실패했습니다: {ex.Message}");
                return null;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 설정 파일 비동기 저장
        /// </summary>
        public async UniTask<JsonConfigResult<T>> SaveAsync(CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await SaveInternalAsync(cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 설정 데이터 비동기 업데이트 (Action을 통한 수정)
        /// </summary>
        public async UniTask<JsonConfigResult<T>> UpdateConfigAsync(Action<T> updateAction, bool autoSave = true,
            CancellationToken cancellationToken = default)
        {
            if (updateAction == null)
                return JsonConfigResult<T>.Failure("업데이트 액션이 null입니다.", JsonConfigError.ValidationError);

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // 설정이 없으면 먼저 비동기로 로드 (안정성 개선)
                if (_config == null)
                {
                    var loadResult = await LoadInternalAsync(true, cancellationToken);
                    if (!loadResult.IsSuccess)
                    {
                        return loadResult;
                    }
                }

                updateAction(_config);

                if (_validator != null && !_validator(_config))
                {
                    return JsonConfigResult<T>.Failure("업데이트된 설정 데이터가 유효성 검사를 통과하지 못했습니다.",
                        JsonConfigError.ValidationError);
                }

                ConfigChanged?.Invoke(_config);

                if (!autoSave) return JsonConfigResult<T>.Success(_config);

                return await SaveInternalAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return JsonConfigResult<T>.Failure("설정 업데이트가 취소되었습니다.", JsonConfigError.Cancelled);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        /// <summary>
        /// 설정을 기본값으로 재설정 (비동기)
        /// </summary>
        public async UniTask<JsonConfigResult<T>> ResetAsync(bool autoSave = true,
            CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                return await CreateAsync(autoSave, cancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        // --------------------------------------------------------------------------------
        // 비동기 내부 헬퍼 메서드
        // --------------------------------------------------------------------------------

        /// <summary>
        /// 실제 비동기 로드 로직 (Semaphore 획득 없이 호출)
        /// </summary>
        private async UniTask<JsonConfigResult<T>> LoadInternalAsync(bool createIfNotExists,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return createIfNotExists ? await CreateAsync(true, cancellationToken) : HandleFileNotFound();
                }

                var jsonContent = await UniTask.RunOnThreadPool(() => File.ReadAllText(_filePath),
                    cancellationToken: cancellationToken);

                if (string.IsNullOrEmpty(jsonContent))
                {
                    return createIfNotExists ? await CreateAsync(true, cancellationToken) : HandleEmptyFile();
                }

                var config = await UniTask.RunOnThreadPool(() => JsonConvert.DeserializeObject<T>(jsonContent),
                    cancellationToken: cancellationToken);

                if (config == null)
                {
                    return createIfNotExists
                        ? await CreateAsync(true, cancellationToken)
                        : HandleParseError("JSON 파싱 결과가 null입니다.");
                }

                if (_validator != null && !_validator(config))
                {
                    return createIfNotExists ? await CreateAsync(true, cancellationToken) : HandleValidationError();
                }

                _config = config;
                return JsonConfigResult<T>.Success(config);
            }
            catch (OperationCanceledException)
            {
                return JsonConfigResult<T>.Failure("JSON 설정 파일 로드가 취소되었습니다.", JsonConfigError.Cancelled);
            }
            catch (JsonException ex)
            {
                return createIfNotExists
                    ? await CreateAsync(true, cancellationToken)
                    : HandleParseError($"JSON 파싱 실패: {ex.Message}");
            }
            catch (Exception ex)
            {
                return createIfNotExists
                    ? await CreateAsync(true, cancellationToken)
                    : HandleIOError($"JSON 설정 파일 로드 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 기본 설정 생성 및 선택적 저장 (비동기)
        /// </summary>
        private async UniTask<JsonConfigResult<T>> CreateAsync(bool autoSave, CancellationToken cancellationToken)
        {
            CreateDefaultConfig();
            if (!autoSave) return JsonConfigResult<T>.Success(_config);

            return await SaveInternalAsync(cancellationToken);
        }

        /// <summary>
        /// 내부 저장 메서드 (락 없이 호출) - 비동기
        /// </summary>
        private async UniTask<JsonConfigResult<T>> SaveInternalAsync(CancellationToken cancellationToken)
        {
            if (_config == null)
                return JsonConfigResult<T>.Failure("저장할 설정 데이터가 없습니다.", JsonConfigError.ValidationError);

            try
            {
                var jsonContent = JsonConvert.SerializeObject(_config, Formatting.Indented);

                await UniTask.RunOnThreadPool(() =>
                {
                    var directoryPath = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    File.WriteAllText(_filePath, jsonContent);
                }, cancellationToken: cancellationToken);

                return JsonConfigResult<T>.Success(_config);
            }
            catch (OperationCanceledException)
            {
                return JsonConfigResult<T>.Failure("JSON 설정 파일 저장이 취소되었습니다.", JsonConfigError.Cancelled);
            }
            catch (Exception ex)
            {
                return JsonConfigResult<T>.Failure($"JSON 설정 파일 저장 실패: {ex.Message}", JsonConfigError.IOError);
            }
        }
#endif

        // --------------------------------------------------------------------------------
        // 동기 메서드
        // --------------------------------------------------------------------------------

        /// <summary>
        /// 유효성 검사기 설정
        /// </summary>
        public void SetValidator(Func<T, bool> validator)
        {
            _validator = validator;
        }

        /// <summary>
        /// 설정 파일 동기 로드
        /// </summary>
        public JsonConfigResult<T> Load(bool createIfNotExists = true)
        {
            lock (_lockObject)
            {
                try
                {
                    if (!File.Exists(_filePath))
                    {
                        return createIfNotExists ? Create(true) : HandleFileNotFound();
                    }

                    var jsonContent = File.ReadAllText(_filePath);

                    if (string.IsNullOrEmpty(jsonContent))
                    {
                        return createIfNotExists ? Create(true) : HandleEmptyFile();
                    }

                    _config = JsonConvert.DeserializeObject<T>(jsonContent);

                    if (_config == null)
                    {
                        return createIfNotExists ? Create(true) : HandleParseError("JSON 파싱 결과가 null입니다.");
                    }

                    if (_validator != null && !_validator(_config))
                    {
                        return createIfNotExists ? Create(true) : HandleValidationError();
                    }

                    return JsonConfigResult<T>.Success(_config);
                }
                catch (JsonException ex)
                {
                    return createIfNotExists ? Create(true) : HandleParseError($"JSON 파싱 실패: {ex.Message}");
                }
                catch (Exception ex)
                {
                    return createIfNotExists ? Create(true) : HandleIOError($"JSON 설정 파일 로드 실패: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 로드된 설정 데이터에서 특정 섹션을 TSection 타입으로 가져옵니다.
        /// </summary>
        public TSection GetSection<TSection>(string sectionName) where TSection : class
        {
            lock (_lockObject)
            {
                if (_config == null)
                {
                    var loadResult = Load();
                    if (!loadResult.IsSuccess)
                    {
                        Debug.LogError("GetSection을 위해 설정을 로드하는 데 실패했습니다.");
                        return null;
                    }
                }

                try
                {
                    var jObject = JObject.FromObject(_config);
                    var sectionToken = jObject[sectionName];

                    if (sectionToken == null)
                    {
                        Debug.LogWarning($"섹션 '{sectionName}'을(를) 찾을 수 없습니다."); // 로직 수정
                        return null;
                    }

                    return sectionToken.ToObject<TSection>();
                }
                catch (Exception ex)
                {
                    Debug.LogError(
                        $"섹션 '{sectionName}'을(를) '{typeof(TSection).Name}' 타입으로 변환하는 데 실패했습니다: {ex.Message}");
                    return null;
                }
            }
        }

        /// <summary>
        /// 설정 파일 동기 저장
        /// </summary>
        public JsonConfigResult<T> Save()
        {
            lock (_lockObject)
            {
                return SaveInternal();
            }
        }

        /// <summary>
        /// 설정 데이터 동기 업데이트 (Action을 통한 수정)
        /// </summary>
        public JsonConfigResult<T> UpdateConfig(Action<T> updateAction, bool autoSave = true)
        {
            if (updateAction == null)
                return JsonConfigResult<T>.Failure("업데이트 액션이 null입니다.", JsonConfigError.ValidationError);

            lock (_lockObject)
            {
                if (_config == null)
                {
                    var loadResult = Load();
                    if (!loadResult.IsSuccess)
                    {
                        return loadResult;
                    }
                }

                updateAction(_config);

                if (_validator != null && !_validator(_config))
                {
                    return JsonConfigResult<T>.Failure("업데이트된 설정 데이터가 유효성 검사를 통과하지 못했습니다.",
                        JsonConfigError.ValidationError);
                }

                ConfigChanged?.Invoke(_config);

                return autoSave ? SaveInternal() : JsonConfigResult<T>.Success(_config);
            }
        }

        /// <summary>
        /// 설정을 기본값으로 재설정 (동기)
        /// </summary>
        public JsonConfigResult<T> Reset(bool autoSave = true)
        {
            lock (_lockObject)
            {
                return Create(autoSave);
            }
        }

        public T GetConfig()
        {
            lock (_lockObject)
            {
                return _config ?? Load().Data;
            }
        }

        public void SetConfig(T config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (_validator != null && !_validator(config))
                throw new ArgumentException("설정 데이터가 유효하지 않습니다.", nameof(config));

            lock (_lockObject)
            {
                _config = config;
                ConfigChanged?.Invoke(_config);
            }
        }

        public bool Exists() => File.Exists(_filePath);

        public bool Delete()
        {
            try
            {
                if (Exists())
                {
                    File.Delete(_filePath);
                }

                lock (_lockObject)
                {
                    _config = null;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"JSON 설정 파일 삭제 실패: {ex.Message}");
                return false;
            }
        }

        public string GetFilePath() => _filePath;

        // --------------------------------------------------------------------------------
        // 동기 내부 헬퍼 메서드
        // --------------------------------------------------------------------------------

        private T CreateDefaultConfig() => _config = new T();

        private JsonConfigResult<T> Create(bool autoSave)
        {
            CreateDefaultConfig();
            return !autoSave ? JsonConfigResult<T>.Success(_config) : SaveInternal();
        }

        private JsonConfigResult<T> SaveInternal()
        {
            if (_config == null)
                return JsonConfigResult<T>.Failure("저장할 설정 데이터가 없습니다.", JsonConfigError.ValidationError);

            try
            {
                var jsonContent = JsonConvert.SerializeObject(_config, Formatting.Indented);
                var directoryPath = Path.GetDirectoryName(_filePath);

                if (!string.IsNullOrEmpty(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                File.WriteAllText(_filePath, jsonContent);
                return JsonConfigResult<T>.Success(_config);
            }
            catch (Exception ex)
            {
                return JsonConfigResult<T>.Failure($"JSON 설정 파일 저장 실패: {ex.Message}", JsonConfigError.IOError);
            }
        }

        // --------------------------------------------------------------------------------
        // 에러 처리 헬퍼
        // --------------------------------------------------------------------------------
        private JsonConfigResult<T> HandleFileNotFound()
        {
            CreateDefaultConfig();
            return JsonConfigResult<T>.Failure($"설정 파일이 존재하지 않습니다. 기본 설정으로 생성했습니다: {_filePath}",
                JsonConfigError.FileNotFound);
        }

        private JsonConfigResult<T> HandleEmptyFile()
        {
            CreateDefaultConfig();
            return JsonConfigResult<T>.Failure("설정 파일이 비어있습니다. 기본 설정으로 생성했습니다.", JsonConfigError.EmptyFile);
        }

        private JsonConfigResult<T> HandleParseError(string message)
        {
            CreateDefaultConfig();
            return JsonConfigResult<T>.Failure(message, JsonConfigError.ParseError);
        }

        private JsonConfigResult<T> HandleValidationError()
        {
            CreateDefaultConfig();
            return JsonConfigResult<T>.Failure("로드된 설정 데이터가 유효성 검사를 통과하지 못했습니다.", JsonConfigError.ValidationError);
        }

        private JsonConfigResult<T> HandleIOError(string message)
        {
            CreateDefaultConfig();
            return JsonConfigResult<T>.Failure(message, JsonConfigError.IOError);
        }

#if USE_UNITASK
        // IDisposable 구현 (SemaphoreSlim 정리용)
        public void Dispose() => _semaphore?.Dispose();
#endif
    }
}