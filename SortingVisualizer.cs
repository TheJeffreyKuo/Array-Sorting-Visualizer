using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using Debug = UnityEngine.Debug;

public class SortingVisualizer : MonoBehaviour
{
    [SerializeField] private GameObject _barPrefab;
    [SerializeField] private RectTransform _graphPanel;
    [SerializeField] private bool _soundEnabled = true;

    private const int SAMPLE_RATE = 44100;
    private const int MAX_OSCILLATORS = 512;

    private BarData[] _bars;
    private Dictionary<int, Stack<Color>> _markedBars = new Dictionary<int, Stack<Color>>();
    private int _arraySize = 100;
    private bool _isSorting = false;
    private float _panelHeight;
    private float _barWidth;
    private AudioSource _audioSource;
    private readonly object _oscillatorLock = new object();
    private List<(float frequency, float startTime, float duration)> _activeOscillators = new List<(float, float, float)>();
    private float _currentTime = 0f;

    [System.Serializable]
    public struct BarData
    {
        public int Value;
        public RectTransform RectTransform;
    }

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = 30;
        InitializeArrays();
        CreateBars();
        RandomizeArray();
        InitializeAudio();
    }

    private void OnApplicationQuit()
    {
        StopAllCoroutines();
        if (_audioSource != null)
        {
            _audioSource.Stop();
            Destroy(_audioSource);
        }
        _markedBars.Clear();
        _activeOscillators.Clear();
        Application.Quit();
    }

    private void InitializeArrays()
    {
        _bars = new BarData[_arraySize];
        for (int i = 0; i < _arraySize; i++)
        {
            _bars[i].Value = i + 1;
        }
    }

    private void CreateBars()
    {
        float panelWidth = _graphPanel.rect.width;
        _panelHeight = _graphPanel.rect.height;
        _barWidth = panelWidth / _arraySize;
        for (int i = 0; i < _arraySize; i++)
        {
            GameObject bar = Instantiate(_barPrefab, _graphPanel);
            _bars[i].RectTransform = bar.GetComponent<RectTransform>();
            float normalizedHeight = ((float)(i + 1) / _arraySize) * _panelHeight;
            float xPosition = i * _barWidth;
            _bars[i].RectTransform.anchorMin = Vector2.zero;
            _bars[i].RectTransform.anchorMax = Vector2.zero;
            _bars[i].RectTransform.pivot = Vector2.zero;
            _bars[i].RectTransform.anchoredPosition = new Vector2(xPosition, 0);
            _bars[i].RectTransform.sizeDelta = new Vector2(_barWidth, normalizedHeight);
        }
    }

    private void InitializeAudio()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0;
        _audioSource.volume = 0.5f;
        _audioSource.Play();
    }

    private float ArrayIndexToFrequency(float normalizedIndex)
    {
        return 120f + 1200f * (normalizedIndex * normalizedIndex);
    }

    private float WaveTriangle(float x)
    {
        x = x % 1.0f;
        if (x <= 0.25f) return 4.0f * x;
        if (x <= 0.75f) return 2.0f - 4.0f * x;
        return 4.0f * x - 4.0f;
    }

    private float EnvelopeADSR(float x)
    {
        const float attack = 0.025f;
        const float decay = 0.1f;
        const float sustain = 0.9f;
        const float release = 0.3f;
        if (x < attack) return x / attack;
        if (x < attack + decay) return 1.0f - (x - attack) / decay * (1.0f - sustain);
        if (x < 1.0f - release) return sustain;
        return sustain / release * (1.0f - x);
    }

    private void AddOscillator(float normalizedValue)
    {
        if (!_soundEnabled) return;
        float frequency = ArrayIndexToFrequency(normalizedValue);
        float duration = 0.1f;
        lock (_oscillatorLock)
        {
            if (_activeOscillators.Count >= MAX_OSCILLATORS)
            {
                _activeOscillators.RemoveAt(0);
            }
            _activeOscillators.Add((frequency, _currentTime, duration));
        }
    }

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (!_soundEnabled) return;
        float deltaTime = 1f / SAMPLE_RATE;
        List<(float frequency, float startTime, float duration)> currentOscillators;
        lock (_oscillatorLock)
        {
            currentOscillators = new List<(float, float, float)>(_activeOscillators);
            _activeOscillators.RemoveAll(osc => _currentTime >= osc.startTime + osc.duration);
        }
        for (int i = 0; i < data.Length; i += channels)
        {
            float sample = 0f;
            int activeCount = 0;
            foreach (var osc in currentOscillators)
            {
                float relativeTime = _currentTime - osc.startTime;
                if (relativeTime >= 0 && relativeTime < osc.duration)
                {
                    float envelope = EnvelopeADSR(relativeTime / osc.duration);
                    sample += envelope * WaveTriangle(relativeTime * osc.frequency);
                    activeCount++;
                }
            }
            if (activeCount > 0)
            {
                sample = (sample / activeCount) * 0.5f;
                for (int c = 0; c < channels; c++)
                {
                    data[i + c] = sample;
                }
            }
            _currentTime += deltaTime;
        }
    }

    private void PlayComparisonSounds(int index1, int index2, int keyValue = -1)
    {
        float normalizedValue1 = _bars[index1].Value / (float)_arraySize;
        float normalizedValue2 = (keyValue == -1 ? _bars[index2].Value : keyValue) / (float)_arraySize;
        AddOscillator(normalizedValue1);
        AddOscillator(normalizedValue2);
    }

    private void ShuffleArray()
    {
        for (int i = _arraySize - 1; i > 0; i--)
        {
            int k = Random.Range(0, i + 1);
            (_bars[k].Value, _bars[i].Value) = (_bars[i].Value, _bars[k].Value);
        }
    }

    public void RandomizeArray()
    {
        if (!_isSorting)
        {
            for (int i = 0; i < _arraySize; i++)
            {
                _bars[i].Value = i + 1;
            }
            ShuffleArray();
            UpdateBarsImmediate();
        }
    }

    public void RandomizeWithEqualValues()
    {
        if (!_isSorting)
        {
            int value = Random.Range(1, _arraySize);
            for (int i = 0; i < _arraySize; i++)
            {
                _bars[i].Value = value;
            }
            int pos1 = Random.Range(0, _arraySize);
            int pos2;
            do
            {
                pos2 = Random.Range(0, _arraySize);
            } while (pos2 == pos1);
            _bars[pos1].Value = Random.Range(1, value);
            _bars[pos2].Value = Random.Range(value + 1, _arraySize + 1);
            UpdateBarsImmediate();
        }
    }

    public void RandomizeWithCubicDistribution()
    {
        if (!_isSorting)
        {
            for (int i = 0; i < _arraySize; i++)
            {
                float normalizedPos = i / (float)_arraySize;
                float cubicValue = normalizedPos * normalizedPos * normalizedPos;
                _bars[i].Value = Mathf.RoundToInt(cubicValue * _arraySize) + 1;
            }
            ShuffleArray();
            UpdateBarsImmediate();
        }
    }

    public void RandomizeWithQuinticDistribution()
    {
        if (!_isSorting)
        {
            for (int i = 0; i < _arraySize; i++)
            {
                float normalizedPos = i / (float)_arraySize;
                float quinticValue = normalizedPos * normalizedPos * normalizedPos * normalizedPos * normalizedPos;
                _bars[i].Value = Mathf.RoundToInt(quinticValue * _arraySize) + 1;
            }
            ShuffleArray();
            UpdateBarsImmediate();
        }
    }

    public void RandomizeDescendingOrder()
    {
        if (!_isSorting)
        {
            for (int i = 0; i < _arraySize; i++)
            {
                _bars[i].Value = _arraySize - i;
            }
            UpdateBarsImmediate();
        }
    }

    private bool AreBarsInRangeWhite(int start, int end)
    {
        if (start < 0 || end >= _arraySize || start > end)
            return false;
        for (int i = start; i <= end; i++)
        {
            Image barImage = _bars[i].RectTransform.GetComponent<Image>();
            if (barImage == null || barImage.color != Color.white)
                return false;
        }
        return true;
    }

    private void UpdateBarsImmediate()
    {
        for (int i = 0; i < _arraySize; i++)
        {
            float normalizedHeight = ((float)_bars[i].Value / _arraySize) * _panelHeight;
            _bars[i].RectTransform.sizeDelta = new Vector2(_barWidth, normalizedHeight);
            ChangeBarColor(i, Color.white);
        }
    }

    private void Swap(int indexA, int indexB)
    {
        (_bars[indexA].Value, _bars[indexB].Value) = (_bars[indexB].Value, _bars[indexA].Value);
        Stack<Color> marksA = null;
        Stack<Color> marksB = null;
        if (_markedBars.ContainsKey(indexA))
        {
            marksA = _markedBars[indexA];
            _markedBars.Remove(indexA);
        }
        if (_markedBars.ContainsKey(indexB))
        {
            marksB = _markedBars[indexB];
            _markedBars.Remove(indexB);
        }
        if (marksA != null) _markedBars[indexB] = marksA;
        if (marksB != null) _markedBars[indexA] = marksB;
        UpdateBarHeight(indexA);
        UpdateBarHeight(indexB);
        if (marksA != null) ChangeBarColor(indexB, marksA.Peek());
        else ChangeBarColor(indexB, Color.white);
        if (marksB != null) ChangeBarColor(indexA, marksB.Peek());
        else ChangeBarColor(indexA, Color.white);
    }

    public void TerminateSorting()
    {
        if (_isSorting)
        {
            _isSorting = false;
            StopAllCoroutines();
            UpdateBarsImmediate();
        }
    }

    public void Mark(int index, Color markColor)
    {
        if (index >= 0 && index < _arraySize)
        {
            if (!_markedBars.ContainsKey(index))
            {
                _markedBars[index] = new Stack<Color>();
            }
            _markedBars[index].Push(markColor);
            ChangeBarColor(index, markColor);
        }
    }

    public void Unmark(int index)
    {
        if (_markedBars.ContainsKey(index))
        {
            _markedBars[index].Pop();
            if (_markedBars[index].Count > 0)
            {
                ChangeBarColor(index, _markedBars[index].Peek());
            }
            else
            {
                _markedBars.Remove(index);
                ChangeBarColor(index, Color.white);
            }
        }
    }

    public void UnmarkByColor(Color color)
    {
        List<int> keysToUpdate = new List<int>();
        foreach (var kvp in _markedBars)
        {
            var stack = kvp.Value;
            var tempStack = new Stack<Color>();
            while (stack.Count > 0)
            {
                if (stack.Peek() != color)
                {
                    tempStack.Push(stack.Pop());
                }
                else
                {
                    stack.Pop();
                }
            }
            while (tempStack.Count > 0)
            {
                stack.Push(tempStack.Pop());
            }
            if (stack.Count == 0)
            {
                keysToUpdate.Add(kvp.Key);
                ChangeBarColor(kvp.Key, Color.white);
            }
            else
            {
                ChangeBarColor(kvp.Key, stack.Peek());
            }
        }
        foreach (int key in keysToUpdate)
        {
            _markedBars.Remove(key);
        }
    }

    public void UnmarkAll()
    {
        List<int> indices = new List<int>(_markedBars.Keys);
        foreach (int index in indices)
        {
            ChangeBarColor(index, Color.white);
        }
        _markedBars.Clear();
    }

    private void ChangeBarColor(int index, Color color)
    {
        Image barImage = _bars[index].RectTransform.GetComponent<Image>();
        if (barImage != null)
        {
            barImage.color = color;
        }
    }

    private void UpdateBarHeight(int index)
    {
        float normalizedHeight = ((float)_bars[index].Value / _arraySize) * _panelHeight;
        _bars[index].RectTransform.sizeDelta = new Vector2(_barWidth, normalizedHeight);
    }

    private IEnumerator SortWithVerification(IEnumerator sortRoutine)
    {
        if (_isSorting) yield break;
        _isSorting = true;
        UnmarkAll();
        yield return sortRoutine;
        UnmarkAll();
        yield return VerifySort();
        _isSorting = false;
    }

    public void StartBubbleSort() => StartCoroutine(SortWithVerification(BubbleSort()));
    public void StartInsertionSort() => StartCoroutine(SortWithVerification(InsertionSort()));
    public void StartSelectionSort() => StartCoroutine(SortWithVerification(SelectionSort()));
    public void StartMergeSort() => StartCoroutine(SortWithVerification(MergeSort(0, _arraySize - 1)));
    public void StartQuickSort() => StartCoroutine(SortWithVerification(QuickSort(0, _arraySize - 1)));

    private IEnumerator BubbleSort()
    {
        for (int i = 0; i < _arraySize - 1; i++)
        {
            for (int j = 0; j < _arraySize - i - 1; j++)
            {
                ChangeBarColor(j + 1, Color.red);
                yield return null;
                ChangeBarColor(j + 1, Color.white);
                ChangeBarColor(j, Color.red);
                yield return null;
                ChangeBarColor(j, Color.white);
                if (_bars[j].Value > _bars[j + 1].Value)
                {
                    ChangeBarColor(j, Color.red);
                    ChangeBarColor(j + 1, Color.red);
                    PlayComparisonSounds(j, j + 1);
                    yield return null;
                    Swap(j, j + 1);
                }
                yield return null;
                ChangeBarColor(j, Color.white);
                ChangeBarColor(j + 1, Color.white);
            }
        }
    }

    private IEnumerator InsertionSort()
    {
        for (int i = 1; i < _arraySize; i++)
        {
            int key = _bars[i].Value;
            UnmarkAll();
            Mark(i, Color.red);
            yield return null;
            Unmark(i);
            Mark(i, Color.green);
            ChangeBarColor(i - 1, Color.red);
            int j = i - 1;
            yield return null;
            while (j >= 0 && _bars[j].Value > key)
            {
                Mark(j, Color.red);
                Mark(j + 1, Color.red);
                yield return null;
                PlayComparisonSounds(j, j + 1);
                Swap(j, j + 1);
                yield return null;
                UnmarkByColor(Color.red);
                j--;
            }
            ChangeBarColor(i - 1, Color.white);
            UnmarkByColor(Color.red);
            if (i != _arraySize - 1)
            {
                Mark(j + 1, Color.red);
            }
            else
            {
                UnmarkAll();
            }
            yield return null;
        }
    }

    private IEnumerator SelectionSort()
    {
        for (int i = 0; i < _arraySize; i++)
        {
            int minIndex = i;
            ChangeBarColor(i, Color.red);
            yield return null;
            for (int j = i + 1; j < _arraySize; j++)
            {
                ChangeBarColor(minIndex, Color.cyan);
                ChangeBarColor(j, Color.red);
                yield return null;
                if (_bars[j].Value < _bars[minIndex].Value)
                {
                    ChangeBarColor(minIndex, Color.white);
                    minIndex = j;
                    ChangeBarColor(minIndex, Color.cyan);
                    if (j + 1 < _arraySize)
                    {
                        ChangeBarColor(j + 1, Color.red);
                    }
                    PlayComparisonSounds(j, minIndex);
                }
                else
                {
                    ChangeBarColor(j, Color.white);
                    ChangeBarColor(minIndex, Color.red);
                    PlayComparisonSounds(j, minIndex);
                    yield return null;
                }
            }
            ChangeBarColor(_arraySize - 1, Color.white);
            Mark(i, Color.red);
            Mark(minIndex, Color.red);
            yield return null;
            if (minIndex != i)
            {
                PlayComparisonSounds(i, minIndex);
                Swap(i, minIndex);
                yield return null;
            }
            UnmarkByColor(Color.red);
            UnmarkByColor(Color.green);
            if (i != _arraySize - 1)
            {
                Mark(i, Color.green);
            }
        }
    }

    private IEnumerator MergeSort(int left, int right)
    {
        if (left < right)
        {
            int mid = (left + right) / 2;
            yield return StartCoroutine(MergeSort(left, mid));
            yield return StartCoroutine(MergeSort(mid + 1, right));
            yield return StartCoroutine(MergeDetailed(left, mid, right));
        }
    }

    private IEnumerator MergeDetailed(int left, int mid, int right)
    {
        int[] tempArray = new int[right - left + 1];
        int i = left;
        int j = mid + 1;
        int k = 0;
        while (i <= mid && j <= right)
        {
            Mark(i, Color.red);
            Mark(j, Color.green);
            PlayComparisonSounds(i, j);
            yield return null;
            if (_bars[i].Value <= _bars[j].Value)
            {
                tempArray[k] = _bars[i].Value;
                Mark(i, Color.cyan);
                yield return null;
                UnmarkAll();
                i++;
            }
            else
            {
                tempArray[k] = _bars[j].Value;
                Mark(j, Color.cyan);
                yield return null;
                UnmarkAll();
                j++;
            }
            k++;
        }
        while (i <= mid)
        {
            PlayComparisonSounds(i, i);
            Mark(i, Color.red);
            yield return null;
            tempArray[k] = _bars[i].Value;
            Mark(i, Color.white);
            if (!AreBarsInRangeWhite(0, _arraySize - 1))
            {
                yield return null;
            }
            i++;
            k++;
        }
        while (j <= right)
        {
            PlayComparisonSounds(j, j);
            Mark(j, Color.green);
            yield return null;
            tempArray[k] = _bars[j].Value;
            Mark(j, Color.white);
            if (!AreBarsInRangeWhite(0, _arraySize - 1))
            {
                yield return null;
            }
            j++;
            k++;
        }
        for (k = 0; k < tempArray.Length; k++)
        {
            PlayComparisonSounds(left + k, left + k);
            Mark(left + k, Color.red);
            yield return null;
            _bars[left + k].Value = tempArray[k];
            UpdateBarHeight(left + k);
            Mark(left + k, Color.white);
            if (!AreBarsInRangeWhite(0, _arraySize - 1))
            {
                yield return null;
            }
        }
    }

    private IEnumerator QuickSort(int low, int high)
    {
        if (low < high)
        {
            int pivot = _bars[high].Value;
            int i = low - 1;
            Mark(high, Color.cyan);
            if (!AreBarsInRangeWhite(0, _arraySize - 2))
            {
                yield return null;
            }
            for (int j = low; j < high; j++)
            {
                Mark(j, Color.red);
                yield return null;
                if (_bars[j].Value < pivot)
                {
                    i++;
                    if (i != j)
                    {
                        Mark(i, Color.red);
                        yield return null;
                        PlayComparisonSounds(i, j);
                        Swap(i, j);
                        yield return null;
                    }
                }
                Unmark(j);
                if (i >= 0) Unmark(i);
                yield return null;
            }
            int pivotFinalPos = i + 1;
            Mark(pivotFinalPos, Color.red);
            yield return null;
            PlayComparisonSounds(pivotFinalPos, high);
            Swap(pivotFinalPos, high);
            yield return null;
            UnmarkAll();
            yield return StartCoroutine(QuickSort(low, pivotFinalPos - 1));
            yield return StartCoroutine(QuickSort(pivotFinalPos + 1, high));
        }
    }

    private IEnumerator VerifySort()
    {
        for (int i = 0; i < _bars.Length - 1; i++)
        {
            ChangeBarColor(i, Color.green);
            float normalizedValue = _bars[i].Value / (float)_arraySize;
            AddOscillator(normalizedValue);
            if (_bars[i].Value > _bars[i + 1].Value)
            {
                Debug.Log("Array is not sorted");
                yield break;
            }
            yield return null;
        }
        ChangeBarColor(_bars.Length - 1, Color.green);
        float finalNormalizedValue = _bars[_bars.Length - 1].Value / (float)_arraySize;
        AddOscillator(finalNormalizedValue);
        yield return new WaitForSeconds(0.5f);
        for (int i = 0; i < _bars.Length; i++)
        {
            ChangeBarColor(i, Color.white);
        }
        Debug.Log("Array is sorted");
    }
}
