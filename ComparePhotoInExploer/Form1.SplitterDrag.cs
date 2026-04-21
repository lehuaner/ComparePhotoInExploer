namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的分割线拖动逻辑——拖拽网格分割线调整视窗大小
/// 普通拖动：改变整条分割线（分割线两侧所有列/行的视窗大小）
/// Shift+拖动：只改变分割线两侧两张图片的视窗大小（单元格级别）
/// </summary>
public partial class Form1
{
    // 每个单元格的宽度比例（归一化，同一行总和=1）
    private float[] _cellWidthRatios = Array.Empty<float>();
    // 每个单元格的高度比例（归一化，同一列总和=1）
    private float[] _cellHeightRatios = Array.Empty<float>();

    // 分割线拖动状态
    private bool _isSplitterDragging = false;
    private int _splitterIndex = -1; // 正在拖动的分割线索引
    private bool _splitterIsVertical = true; // true=垂直分割线, false=水平分割线
    private int _hoverSplitterIndex = -1; // 悬停的分割线索引
    private bool _hoverSplitterIsVertical = true; // 悬停分割线方向
    private int _hoverSplitterRow = -1; // 悬停的垂直分割线所在行
    private int _hoverSplitterCol = -1; // 悬停的水平分割线所在列
    private int _splitterRow = -1; // 拖动中的垂直分割线所在行（Shift模式下确定是哪两个单元格）
    private int _splitterCol = -1; // 拖动中的水平分割线所在列（Shift模式下确定是哪两个单元格）

    // 分割线检测阈值（像素）
    private const int SplitterHitTestRadius = 5;

    // 单个视窗最小宽度/高度（像素）
    private const int MinCellSize = 80;

    /// <summary>
    /// 初始化单元格比例数组（在布局变化时调用）
    /// </summary>
    private void InitSplitters()
    {
        int total = Math.Max(1, _cols * _rows);
        _cellWidthRatios = new float[total];
        _cellHeightRatios = new float[total];
        ResetSplitters();
    }

    /// <summary>
    /// 重置所有单元格比例为均分
    /// </summary>
    private void ResetSplitters()
    {
        for (int i = 0; i < _cellWidthRatios.Length; i++)
        {
            _cellWidthRatios[i] = 1f / _cols;
            _cellHeightRatios[i] = 1f / _rows;
        }
    }

    /// <summary>
    /// 检测鼠标位置是否在分割线上，返回分割线信息
    /// </summary>
    private (bool isVertical, int splitterIndex, int rowOrCol) HitTestSplitter(PointF pos)
    {
        if (_imageCount <= 1)
            return (false, -1, -1);

        // 检测垂直分割线（在列 c 和列 c+1 之间，splitterIndex = c）
        for (int c = 0; c < _cols - 1; c++)
        {
            for (int r = 0; r < _rows; r++)
            {
                int cellIdx = r * _cols + c;
                float lineX = GetCellLeft(cellIdx) + GetCellWidth(cellIdx);
                float cellTop = GetCellTop(cellIdx);
                float cellBottom = cellTop + GetCellHeight(cellIdx);

                float dist = Math.Abs(pos.X - lineX);
                if (dist <= SplitterHitTestRadius && pos.Y >= cellTop && pos.Y <= cellBottom)
                {
                    return (true, c, r);
                }
            }
        }

        // 检测水平分割线（在行 r 和行 r+1 之间，splitterIndex = r）
        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                int cellIdx = r * _cols + c;
                float lineY = GetCellTop(cellIdx) + GetCellHeight(cellIdx);
                float cellLeft = GetCellLeft(cellIdx);
                float cellRight = cellLeft + GetCellWidth(cellIdx);

                float dist = Math.Abs(pos.Y - lineY);
                if (dist <= SplitterHitTestRadius && pos.X >= cellLeft && pos.X <= cellRight)
                {
                    return (false, r, c);
                }
            }
        }

        return (false, -1, -1);
    }

    /// <summary>
    /// 获取单元格的左边界 X 坐标
    /// </summary>
    private float GetCellLeft(int cellIndex)
    {
        int row = cellIndex / _cols;
        float totalW = this.ClientSize.Width;
        float x = 0;
        for (int c = 0; c < cellIndex % _cols; c++)
        {
            x += _cellWidthRatios[row * _cols + c] * totalW;
        }
        return x;
    }

    /// <summary>
    /// 获取单元格的上边界 Y 坐标
    /// </summary>
    private float GetCellTop(int cellIndex)
    {
        int col = cellIndex % _cols;
        float totalH = this.ClientSize.Height - TitleBarHeight;
        float y = TitleBarHeight;
        for (int r = 0; r < cellIndex / _cols; r++)
        {
            y += _cellHeightRatios[r * _cols + col] * totalH;
        }
        return y;
    }

    /// <summary>
    /// 获取单元格的宽度
    /// </summary>
    private float GetCellWidth(int cellIndex)
    {
        return _cellWidthRatios[cellIndex] * this.ClientSize.Width;
    }

    /// <summary>
    /// 获取单元格的高度
    /// </summary>
    private float GetCellHeight(int cellIndex)
    {
        return _cellHeightRatios[cellIndex] * (this.ClientSize.Height - TitleBarHeight);
    }

    /// <summary>
    /// 开始拖动分割线
    /// </summary>
    private void StartSplitterDrag(bool isVertical, int splitterIndex, int rowOrCol)
    {
        _isSplitterDragging = true;
        _splitterIsVertical = isVertical;
        _splitterIndex = splitterIndex;
        if (isVertical)
            _splitterRow = rowOrCol;
        else
            _splitterCol = rowOrCol;
    }

    /// <summary>
    /// 实时检测 Shift 键是否按下
    /// </summary>
    private static bool IsShiftPressed()
    {
        return (NativeMethods.GetAsyncKeyState(Keys.ShiftKey) & 0x8000) != 0;
    }

    /// <summary>
    /// 拖动分割线时更新比例（实时检测 Shift 状态）
    /// </summary>
    private void UpdateSplitterDrag(float deltaX, float deltaY)
    {
        if (!_isSplitterDragging || _splitterIndex < 0)
            return;

        bool shiftMode = IsShiftPressed();

        if (_splitterIsVertical)
            ApplyVerticalSplitterDelta(deltaX, shiftMode);
        else
            ApplyHorizontalSplitterDelta(deltaY, shiftMode);
    }

    /// <summary>
    /// 垂直分割线拖动：调整单元格宽度比例
    /// 普通模式：所有行的该分割线一起动（整列变化）
    /// Shift模式：只改变分割线所在行的两个单元格
    /// </summary>
    private void ApplyVerticalSplitterDelta(float deltaX, bool shiftMode)
    {
        float totalW = this.ClientSize.Width;
        if (totalW <= 0) return;
        float deltaRatio = deltaX / totalW;
        float minRatio = (float)MinCellSize / totalW;

        if (shiftMode)
        {
            // Shift 模式：只改变分割线所在行的两个单元格
            int leftCell = _splitterRow * _cols + _splitterIndex;
            int rightCell = _splitterRow * _cols + _splitterIndex + 1;

            float newLeft = _cellWidthRatios[leftCell] + deltaRatio;
            float newRight = _cellWidthRatios[rightCell] - deltaRatio;

            if (newLeft < minRatio || newRight < minRatio)
                return;

            _cellWidthRatios[leftCell] = newLeft;
            _cellWidthRatios[rightCell] = newRight;
        }
        else
        {
            // 普通模式：所有行的该分割线一起动（整列变化）
            for (int r = 0; r < _rows; r++)
            {
                float leftTotal = 0f;
                for (int c = 0; c <= _splitterIndex; c++)
                    leftTotal += _cellWidthRatios[r * _cols + c];

                if (leftTotal <= 0) continue;

                float rightTotal = 0f;
                for (int c = _splitterIndex + 1; c < _cols; c++)
                    rightTotal += _cellWidthRatios[r * _cols + c];

                if (rightTotal <= 0) continue;

                float newLeftTotal = leftTotal + deltaRatio;
                float newRightTotal = rightTotal - deltaRatio;

                if (newLeftTotal < minRatio || newRightTotal < minRatio)
                    continue;

                float leftScale = newLeftTotal / leftTotal;
                for (int c = 0; c <= _splitterIndex; c++)
                    _cellWidthRatios[r * _cols + c] *= leftScale;

                float rightScale = newRightTotal / rightTotal;
                for (int c = _splitterIndex + 1; c < _cols; c++)
                    _cellWidthRatios[r * _cols + c] *= rightScale;
            }
        }
    }

    /// <summary>
    /// 水平分割线拖动：调整单元格高度比例
    /// 普通模式：所有列的该分割线一起动（整行变化）
    /// Shift模式：只改变分割线所在列的两个单元格
    /// </summary>
    private void ApplyHorizontalSplitterDelta(float deltaY, bool shiftMode)
    {
        float totalH = this.ClientSize.Height - TitleBarHeight;
        if (totalH <= 0) return;
        float deltaRatio = deltaY / totalH;
        float minRatio = (float)MinCellSize / totalH;

        if (shiftMode)
        {
            // Shift 模式：只改变分割线所在列的两个单元格
            int topCell = _splitterIndex * _cols + _splitterCol;
            int bottomCell = (_splitterIndex + 1) * _cols + _splitterCol;

            float newTop = _cellHeightRatios[topCell] + deltaRatio;
            float newBottom = _cellHeightRatios[bottomCell] - deltaRatio;

            if (newTop < minRatio || newBottom < minRatio)
                return;

            _cellHeightRatios[topCell] = newTop;
            _cellHeightRatios[bottomCell] = newBottom;
        }
        else
        {
            // 普通模式：所有列的该分割线一起动（整行变化）
            for (int c = 0; c < _cols; c++)
            {
                float topTotal = 0f;
                for (int r = 0; r <= _splitterIndex; r++)
                    topTotal += _cellHeightRatios[r * _cols + c];

                if (topTotal <= 0) continue;

                float bottomTotal = 0f;
                for (int r = _splitterIndex + 1; r < _rows; r++)
                    bottomTotal += _cellHeightRatios[r * _cols + c];

                if (bottomTotal <= 0) continue;

                float newTopTotal = topTotal + deltaRatio;
                float newBottomTotal = bottomTotal - deltaRatio;

                if (newTopTotal < minRatio || newBottomTotal < minRatio)
                    continue;

                float topScale = newTopTotal / topTotal;
                for (int r = 0; r <= _splitterIndex; r++)
                    _cellHeightRatios[r * _cols + c] *= topScale;

                float bottomScale = newBottomTotal / bottomTotal;
                for (int r = _splitterIndex + 1; r < _rows; r++)
                    _cellHeightRatios[r * _cols + c] *= bottomScale;
            }
        }
    }

    /// <summary>
    /// 约束所有列比例，确保每列不小于 MinCellSize
    /// </summary>
    private void ClampAllColSplits()
    {
        if (_cellWidthRatios.Length == 0) return;
        float totalW = this.ClientSize.Width;
        if (totalW <= 0) return;
        float minRatio = (float)MinCellSize / totalW;

        for (int i = 0; i < _cellWidthRatios.Length; i++)
        {
            if (_cellWidthRatios[i] < minRatio)
                _cellWidthRatios[i] = minRatio;
        }

        // 按行归一化使每行宽度总和=1
        for (int r = 0; r < _rows; r++)
        {
            float sum = 0f;
            for (int c = 0; c < _cols; c++)
                sum += _cellWidthRatios[r * _cols + c];
            if (sum > 0)
            {
                for (int c = 0; c < _cols; c++)
                    _cellWidthRatios[r * _cols + c] /= sum;
            }
        }
    }

    /// <summary>
    /// 约束所有行比例，确保每行不小于 MinCellSize
    /// </summary>
    private void ClampAllRowSplits()
    {
        if (_cellHeightRatios.Length == 0) return;
        float totalH = this.ClientSize.Height - TitleBarHeight;
        if (totalH <= 0) return;
        float minRatio = (float)MinCellSize / totalH;

        for (int i = 0; i < _cellHeightRatios.Length; i++)
        {
            if (_cellHeightRatios[i] < minRatio)
                _cellHeightRatios[i] = minRatio;
        }

        // 按列归一化使每列高度总和=1
        for (int c = 0; c < _cols; c++)
        {
            float sum = 0f;
            for (int r = 0; r < _rows; r++)
                sum += _cellHeightRatios[r * _cols + c];
            if (sum > 0)
            {
                for (int r = 0; r < _rows; r++)
                    _cellHeightRatios[r * _cols + c] /= sum;
            }
        }
    }

    /// <summary>
    /// 结束分割线拖动
    /// </summary>
    private void EndSplitterDrag()
    {
        _isSplitterDragging = false;
        _splitterIndex = -1;
        _splitterRow = -1;
        _splitterCol = -1;
    }

    /// <summary>
    /// 获取悬停分割线的光标类型
    /// </summary>
    private Cursor GetSplitterCursor(bool isVertical) => isVertical ? Cursors.SizeWE : Cursors.SizeNS;

    /// <summary>
    /// 计算所有单元格区域之间的"华容道"空隙区域（Shift拖动后可能出现的未覆盖区域）
    /// </summary>
    private List<RectangleF> GetGapRegions()
    {
        var gaps = new List<RectangleF>();
        if (_cols <= 1 || _rows <= 1)
            return gaps;

        // 收集每行各列的右边界 X 坐标
        var rowRightEdges = new float[_rows, _cols]; // 每行每列的右边界
        for (int r = 0; r < _rows; r++)
        {
            float x = 0;
            for (int c = 0; c < _cols; c++)
            {
                float w = _cellWidthRatios[r * _cols + c] * this.ClientSize.Width;
                x += w;
                rowRightEdges[r, c] = x;
            }
        }

        // 收集每列各行的底边界 Y 坐标
        var colBottomEdges = new float[_rows, _cols]; // 每列每行的底边界
        for (int c = 0; c < _cols; c++)
        {
            float y = TitleBarHeight;
            for (int r = 0; r < _rows; r++)
            {
                float h = _cellHeightRatios[r * _cols + c] * (this.ClientSize.Height - TitleBarHeight);
                y += h;
                colBottomEdges[r, c] = y;
            }
        }

        // 检测每个"交叉区域"：第 r1 行的第 c1 列右边界和第 r2 行的第 c2 列右边界之间
        // 以及第 c1 列的第 r1 行底边界和第 c2 列的第 r2 行底边界之间形成的矩形
        // 简化：遍历所有内部网格交叉点，检查是否有空隙
        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c < _cols - 1; c++)
            {
                // 当前格子(r,c)的右下角
                float rightEdge = rowRightEdges[r, c];
                float bottomEdge = colBottomEdges[r, c];

                // 右邻格子(r,c+1)的右边界 和 下邻格子(r+1,c)的底边界
                float rightNeighborRight = rowRightEdges[r, c + 1];
                float bottomNeighborBottom = colBottomEdges[r + 1, c];

                // 对角格子(r+1,c+1)的左上角
                float diagLeft = rowRightEdges[r + 1, c]; // 第r+1行第c列右边界 = 第r+1行第c+1列左边界
                float diagTop = colBottomEdges[r, c + 1];  // 第c+1列第r行底边界 = 第c+1列第r+1行顶边界

                // 检查是否有空隙：中间区域
                // 空隙区域在 (rightEdge, bottomEdge) 和 (diagLeft, diagTop) 之间
                float gapX = Math.Min(rightEdge, diagLeft);
                float gapY = Math.Min(bottomEdge, diagTop);
                float gapRight = Math.Max(rightEdge, diagLeft);
                float gapBottom = Math.Max(bottomEdge, diagTop);

                float gapW = gapRight - gapX;
                float gapH = gapBottom - gapY;

                if (gapW > 0.5f && gapH > 0.5f)
                {
                    gaps.Add(new RectangleF(gapX, gapY, gapW, gapH));
                }
            }
        }

        return gaps;
    }
}
