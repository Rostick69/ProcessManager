// ========================================================
// Лабораторная работа №2
// Диспетчер процессов с управлением приоритетами и CPU Affinity
// ========================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ProcessManagerLab.Models;
using ProcessManagerLab.Services;
using ProcessManagerLab.Utilities;

namespace ProcessManagerLab
{
    public partial class MainWindow : Window
    {
        private const double MaxBarLength = 400; // длина самого длинного столбца диаграммы, px

        private readonly ProcessService _service = new ProcessService();
        private readonly DispatcherTimer _timer;

        private List<ProcessInfo> _allProcesses = new List<ProcessInfo>(); // последний полный список
        private int? _selectedPid;           // PID выбранного процесса (сохраняется между обновлениями)
        private CheckBox[] _coreCheckBoxes;  // чекбоксы ядер для CPU Affinity
        private bool _isLoading;             // защита от одновременных обновлений

        // Узлы дерева по PID и PID раскрытых узлов (чтобы дерево не сворачивалось при обновлении)
        private readonly Dictionary<int, TreeViewItem> _treeItems = new Dictionary<int, TreeViewItem>();
        private readonly HashSet<int> _expandedPids = new HashSet<int>();
        private bool _selectingFromCode;     // чтобы выбор в дереве и таблице не вызывал друг друга по кругу

        public MainWindow()
        {
            InitializeComponent();

            // Шесть уровней приоритета процесса в Windows
            cbPriority.ItemsSource = new[]
            {
                new { Key = "Idle (низкий)", Value = ProcessPriorityClass.Idle },
                new { Key = "BelowNormal", Value = ProcessPriorityClass.BelowNormal },
                new { Key = "Normal", Value = ProcessPriorityClass.Normal },
                new { Key = "AboveNormal", Value = ProcessPriorityClass.AboveNormal },
                new { Key = "High (высокий)", Value = ProcessPriorityClass.High },
                new { Key = "RealTime (реального времени)", Value = ProcessPriorityClass.RealTime }
            };

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += async (s, e) => await RefreshAsync();

            Loaded += async (s, e) =>
            {
                await RefreshAsync();
                _timer.Start();
            };
        }

        // ==================== ОБНОВЛЕНИЕ ДАННЫХ ====================

        // Получение процессов выполняется в фоновом потоке (Task.Run), чтобы интерфейс не зависал
        private async Task RefreshAsync()
        {
            if (_isLoading) return;
            _isLoading = true;
            var sw = Stopwatch.StartNew();
            try
            {
                var processes = await Task.Run(() => _service.GetAllProcesses());
                var coreLoads = await Task.Run(() => _service.GetCoreLoads());
                sw.Stop();

                _allProcesses = processes;
                BuildTree();
                ApplyFilter();
                UpdateCoreChart(coreLoads);
                UpdateMemoryChart();

                tbStatus.Text = $"Обновлено в {DateTime.Now:HH:mm:ss} за {sw.ElapsedMilliseconds} мс. " +
                                $"Всего процессов: {processes.Count}, ядер: {_service.CoreCount}";
            }
            catch (Exception ex)
            {
                tbStatus.Text = "Ошибка обновления: " + ex.Message;
            }
            finally
            {
                _isLoading = false;
            }
        }

        // Поиск и фильтрация. Сортировку, выбранную пользователем щелчком по заголовку, сохраняем
        private void ApplyFilter()
        {
            IEnumerable<ProcessInfo> query = _allProcesses;

            string search = txtSearch.Text.Trim();
            if (search.Length > 0)
                query = query.Where(p => p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                                         || p.Id.ToString() == search);

            if (cbFilter.SelectedIndex == 1) query = query.Where(p => p.HasWindow);
            else if (cbFilter.SelectedIndex == 2) query = query.Where(p => p.IsSystem);

            var filtered = query.ToList();

            // Запоминаем текущую сортировку
            var oldView = dgProcesses.ItemsSource == null ? null : CollectionViewSource.GetDefaultView(dgProcesses.ItemsSource);
            var sorts = oldView?.SortDescriptions.ToList() ?? new List<SortDescription>();
            if (sorts.Count == 0)
                sorts.Add(new SortDescription("Name", ListSortDirection.Ascending));

            _selectingFromCode = true;
            dgProcesses.ItemsSource = filtered;
            var view = CollectionViewSource.GetDefaultView(filtered);
            foreach (var sd in sorts) view.SortDescriptions.Add(sd);

            // Значок сортировки в заголовке столбца
            foreach (var col in dgProcesses.Columns)
            {
                var sd = sorts.FirstOrDefault(x => x.PropertyName == col.SortMemberPath);
                col.SortDirection = sd.PropertyName == null ? (ListSortDirection?)null : sd.Direction;
            }

            tbCount.Text = filtered.Count.ToString();

            // Восстанавливаем выделение по PID
            var selected = _selectedPid.HasValue ? filtered.FirstOrDefault(p => p.Id == _selectedPid.Value) : null;
            dgProcesses.SelectedItem = selected;
            _selectingFromCode = false;

            if (selected != null)
                ShowDetails(selected, false);
            else if (_selectedPid.HasValue && !_allProcesses.Any(p => p.Id == _selectedPid.Value))
                ClearDetails(); // выбранный процесс завершился
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshAsync();
        private void txtSearch_TextChanged(object sender, TextChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }
        private void cbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilter(); }

        private void cbInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_timer == null) return; // событие приходит ещё во время InitializeComponent
            if (cbInterval.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag.ToString(), out int sec))
                _timer.Interval = TimeSpan.FromSeconds(sec);
        }

        // ==================== ВЫБРАННЫЙ ПРОЦЕСС ====================

        private void dgProcesses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectingFromCode) return;
            if (dgProcesses.SelectedItem is ProcessInfo p)
            {
                bool isNew = p.Id != _selectedPid;
                _selectedPid = p.Id;
                ShowDetails(p, isNew);
                SelectInTree(p.Id);
            }
        }

        // Заполнение нижней панели: сведения, приоритет, Affinity, потоки.
        // rebuildAffinity = true только при выборе другого процесса, чтобы не сбрасывать отмеченные, но не применённые ядра
        private void ShowDetails(ProcessInfo p, bool rebuildAffinity)
        {
            var parent = _allProcesses.FirstOrDefault(x => x.Id == p.ParentId);
            string parentText = parent != null ? $"{parent.Name} (PID {parent.Id})"
                              : p.ParentId > 0 ? $"PID {p.ParentId} (уже завершён)" : "нет";

            tbSelectedInfo.Text =
                $"PID: {p.Id}\n" +
                $"Имя: {p.Name}\n" +
                $"Приоритет: {p.PriorityString}\n" +
                $"Память: {p.MemoryMB} МБ\n" +
                $"Потоков: {p.ThreadCount}\n" +
                $"Время CPU: {p.CpuTimeString}, загрузка {p.CpuPercent:F1}%\n" +
                $"Родительский процесс: {parentText}\n" +
                $"Дочерних процессов: {p.ChildCount}\n" +
                $"Тип: {(p.IsSystem ? "системный (сессия 0)" : "пользовательский")}{(p.HasWindow ? ", есть окно" : "")}";

            btnGoParent.IsEnabled = parent != null;
            if (rebuildAffinity)
            {
                cbPriority.SelectedValue = p.Priority;
                UpdateAffinityUI(p);
            }
            dgThreads.ItemsSource = _service.GetThreads(p.Id);
            HighlightParent(p.ParentId);
        }

        private void ClearDetails()
        {
            _selectedPid = null;
            tbSelectedInfo.Text = "Выбранный процесс завершился. Выберите другой процесс";
            btnGoParent.IsEnabled = false;
            cbPriority.SelectedValue = null;
            spCores.Children.Clear();
            _coreCheckBoxes = null;
            tbAffinityInfo.Text = "Текущая маска: —";
            tbNewMask.Text = "";
            dgThreads.ItemsSource = null;
        }

        private ProcessInfo SelectedProcess =>
            _selectedPid.HasValue ? _allProcesses.FirstOrDefault(p => p.Id == _selectedPid.Value) : null;

        // ==================== ПРИОРИТЕТ ====================

        private void btnSetPriority_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProcess;
            if (p == null || !(cbPriority.SelectedValue is ProcessPriorityClass newPriority))
            {
                MessageBox.Show("Выберите процесс и приоритет", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Предупреждение для критического приоритета
            if (newPriority == ProcessPriorityClass.RealTime)
            {
                var res = MessageBox.Show(
                    "Приоритет RealTime выше, чем у системных потоков ввода-вывода.\n" +
                    "Нагруженный процесс с таким приоритетом может «заморозить» мышь, клавиатуру и всю систему.\n\nПродолжить?",
                    "Внимание: приоритет реального времени", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res != MessageBoxResult.Yes) return;
            }

            string error = _service.SetPriority(p.Id, newPriority);
            if (error == null)
            {
                tbStatus.Text = $"Приоритет процесса {p.Name} (PID {p.Id}) изменён на {newPriority}";
                _ = RefreshAsync();
            }
            else
            {
                MessageBox.Show(error, "Не удалось изменить приоритет", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== CPU AFFINITY ====================

        private void UpdateAffinityUI(ProcessInfo p)
        {
            spCores.Children.Clear();
            int count = _service.CoreCount;
            _coreCheckBoxes = new CheckBox[count];

            if (!p.ProcessorAffinity.HasValue)
            {
                tbAffinityInfo.Text = "Текущая маска: нет доступа (нужны права администратора)";
                tbNewMask.Text = "";
                return;
            }

            IntPtr mask = p.ProcessorAffinity.Value;
            tbAffinityInfo.Text = $"Текущая маска: {AffinityHelper.ToBinary(mask, count)} (двоичная), " +
                                  $"{AffinityHelper.ToHex(mask)} (шестнадцатеричная)";

            for (int i = 0; i < count; i++)
            {
                var cb = new CheckBox
                {
                    Content = $"Ядро {i}",
                    Margin = new Thickness(0, 0, 15, 5),
                    IsChecked = AffinityHelper.IsCoreEnabled(mask, i)
                };
                cb.Checked += (s, e) => UpdateNewMaskText();
                cb.Unchecked += (s, e) => UpdateNewMaskText();
                _coreCheckBoxes[i] = cb;
                spCores.Children.Add(cb);
            }
            UpdateNewMaskText();
        }

        private bool[] GetSelectedCores() => _coreCheckBoxes.Select(cb => cb.IsChecked == true).ToArray();

        // Предпросмотр новой маски по отмеченным чекбоксам
        private void UpdateNewMaskText()
        {
            if (_coreCheckBoxes == null || _coreCheckBoxes.Any(cb => cb == null)) return;
            IntPtr newMask = AffinityHelper.SetCoreMask(GetSelectedCores());
            tbNewMask.Text = "Новая маска: " + AffinityHelper.GetMaskAsString(newMask, _coreCheckBoxes.Length);
        }

        private void btnAllCores_Click(object sender, RoutedEventArgs e)
        {
            if (_coreCheckBoxes == null) return;
            foreach (var cb in _coreCheckBoxes.Where(c => c != null)) cb.IsChecked = true;
        }

        private void btnApplyAffinity_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProcess;
            if (p == null || _coreCheckBoxes == null || _coreCheckBoxes.Any(cb => cb == null))
            {
                MessageBox.Show("Сначала выберите процесс, к маске которого есть доступ", "Внимание",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IntPtr newMask = AffinityHelper.SetCoreMask(GetSelectedCores());
            string error = _service.SetAffinity(p.Id, newMask);
            if (error == null)
            {
                p.ProcessorAffinity = newMask;
                tbAffinityInfo.Text = $"Текущая маска: {AffinityHelper.ToBinary(newMask, _coreCheckBoxes.Length)} (двоичная), " +
                                      $"{AffinityHelper.ToHex(newMask)} (шестнадцатеричная)";
                tbStatus.Text = $"CPU Affinity процесса {p.Name} изменён: {AffinityHelper.GetMaskAsString(newMask, _coreCheckBoxes.Length)}";
            }
            else
            {
                MessageBox.Show(error, "Не удалось изменить CPU Affinity", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== ЗАВЕРШЕНИЕ ПРОЦЕССА ====================

        private void btnKill_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProcess;
            if (p == null)
            {
                MessageBox.Show("Выберите процесс", "Внимание", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string warning = $"Завершить процесс {p.Name} (PID {p.Id})?\nНесохранённые данные в нём будут потеряны.";
            if (p.IsSystem)
                warning = $"ВНИМАНИЕ: {p.Name} — системный процесс (сессия 0).\n" +
                          "Его завершение может привести к сбою или перезагрузке Windows.\n\nВсё равно завершить?";

            if (MessageBox.Show(warning, "Подтверждение", MessageBoxButton.YesNo,
                    p.IsSystem ? MessageBoxImage.Warning : MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            string error = _service.Kill(p.Id);
            if (error == null)
            {
                tbStatus.Text = $"Процесс {p.Name} (PID {p.Id}) завершён";
                _ = RefreshAsync();
            }
            else
            {
                MessageBox.Show(error, "Не удалось завершить процесс", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ==================== ДЕРЕВО ПРОЦЕССОВ ====================

        // Построение дерева по ParentId. Раскрытые узлы и выделение сохраняются
        private void BuildTree()
        {
            // Запоминаем раскрытые узлы
            foreach (var kv in _treeItems)
            {
                if (kv.Value.IsExpanded) _expandedPids.Add(kv.Key);
                else _expandedPids.Remove(kv.Key);
            }

            var dict = _allProcesses.ToDictionary(p => p.Id);
            foreach (var p in _allProcesses) p.Children.Clear();
            foreach (var p in _allProcesses)
            {
                // Защита от ошибочных связей: процесс не может быть родителем сам себе
                if (p.ParentId > 0 && p.ParentId != p.Id && dict.ContainsKey(p.ParentId))
                    dict[p.ParentId].Children.Add(p);
            }

            // Корни: процессы без живого родителя
            var roots = _allProcesses.Where(p => p.ParentId <= 0 || p.ParentId == p.Id || !dict.ContainsKey(p.ParentId))
                                     .OrderBy(p => p.Name);

            _selectingFromCode = true;
            tvProcessTree.Items.Clear();
            _treeItems.Clear();
            foreach (var root in roots)
                tvProcessTree.Items.Add(CreateTreeItem(root, 0));
            _selectingFromCode = false;

            if (_selectedPid.HasValue) SelectInTree(_selectedPid.Value);
        }

        private TreeViewItem CreateTreeItem(ProcessInfo proc, int depth)
        {
            string children = proc.ChildCount > 0 ? $" — дочерних: {proc.ChildCount}" : "";
            var item = new TreeViewItem
            {
                Header = $"{proc.Name} (PID {proc.Id}){children}",
                Tag = proc.Id,
                IsExpanded = _expandedPids.Contains(proc.Id)
            };
            _treeItems[proc.Id] = item;

            // Ограничение глубины на случай циклических ссылок (PID могут переиспользоваться Windows)
            if (depth < 30)
            {
                foreach (var child in proc.Children.OrderBy(c => c.Name))
                    item.Items.Add(CreateTreeItem(child, depth + 1));
            }

            item.Selected += TreeItem_Selected;
            return item;
        }

        private void TreeItem_Selected(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // событие Selected «всплывает» к родительским узлам — останавливаем
            if (_selectingFromCode) return;
            if (!(sender is TreeViewItem item) || !(item.Tag is int pid)) return;

            // Если процесс скрыт фильтром — сбрасываем фильтр и поиск
            var shown = dgProcesses.ItemsSource as IEnumerable<ProcessInfo>;
            if (shown == null || !shown.Any(p => p.Id == pid))
            {
                _selectedPid = pid;
                txtSearch.Text = "";
                cbFilter.SelectedIndex = 0;
            }

            _selectedPid = pid;
            ApplyFilter();
            var p2 = SelectedProcess;
            if (p2 != null)
            {
                ShowDetails(p2, true);
                dgProcesses.ScrollIntoView(p2);
            }
        }

        // Выделить узел в дереве и раскрыть путь к нему
        private void SelectInTree(int pid)
        {
            if (!_treeItems.TryGetValue(pid, out var item)) return;
            _selectingFromCode = true;
            var parent = item.Parent as TreeViewItem;
            while (parent != null)
            {
                parent.IsExpanded = true;
                parent = parent.Parent as TreeViewItem;
            }
            item.IsSelected = true;
            item.BringIntoView();
            _selectingFromCode = false;
        }

        // Выделение родительского процесса выбранного процесса в дереве (жирный шрифт и цвет)
        private void HighlightParent(int parentPid)
        {
            foreach (var kv in _treeItems)
            {
                bool isParent = kv.Key == parentPid;
                kv.Value.FontWeight = isParent ? FontWeights.Bold : FontWeights.Normal;
                kv.Value.Foreground = isParent ? Brushes.DarkOrange : Brushes.Black;
            }
        }

        private void btnGoParent_Click(object sender, RoutedEventArgs e)
        {
            var p = SelectedProcess;
            if (p == null || !_treeItems.ContainsKey(p.ParentId)) return;
            _selectedPid = p.ParentId;
            txtSearch.Text = "";
            cbFilter.SelectedIndex = 0;
            ApplyFilter();
            var parent = SelectedProcess;
            if (parent != null)
            {
                ShowDetails(parent, true);
                SelectInTree(parent.Id);
                dgProcesses.ScrollIntoView(parent);
            }
        }

        // ==================== ДИАГРАММЫ ====================

        // Загрузка каждого логического ядра
        private void UpdateCoreChart(List<double> loads)
        {
            icCores.ItemsSource = loads.Select((load, i) => new ChartBar
            {
                Label = $"Ядро {i}",
                Value = load,
                ValueText = $"{load:F0}%",
                BarLength = Math.Max(1, load / 100.0 * MaxBarLength)
            }).ToList();
        }

        // Распределение памяти: 10 процессов, занимающих больше всего памяти
        private void UpdateMemoryChart()
        {
            var top = _allProcesses.OrderByDescending(p => p.MemoryMB).Take(10).ToList();
            double max = top.Count > 0 ? Math.Max(1, top[0].MemoryMB) : 1;
            long total = _allProcesses.Sum(p => p.MemoryMB);

            icMemory.ItemsSource = top.Select(p => new ChartBar
            {
                Label = $"{p.Name} ({p.Id})",
                Value = p.MemoryMB,
                ValueText = $"{p.MemoryMB} МБ ({(total > 0 ? p.MemoryMB * 100.0 / total : 0):F1}% от суммы)",
                BarLength = Math.Max(1, p.MemoryMB / max * MaxBarLength)
            }).ToList();
        }
    }
}
