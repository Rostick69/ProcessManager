using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
    // Главное окно приложения "Диспетчер процессов"
    public partial class MainWindow : Window
    {
        private readonly ProcessService _service = new ProcessService();
        private DispatcherTimer _timer;
        private ProcessInfo _selectedProcess;
        private CheckBox[] _coreCheckBoxes;

        public MainWindow()
        {
            InitializeComponent();

            // Заполняем выпадающий список приоритетов
            cbPriority.ItemsSource = new[]
            {
                new { Key = "Idle", Value = ProcessPriorityClass.Idle },
                new { Key = "BelowNormal", Value = ProcessPriorityClass.BelowNormal },
                new { Key = "Normal", Value = ProcessPriorityClass.Normal },
                new { Key = "AboveNormal", Value = ProcessPriorityClass.AboveNormal },
                new { Key = "High", Value = ProcessPriorityClass.High },
                new { Key = "RealTime", Value = ProcessPriorityClass.RealTime }
            };

            LoadProcesses();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _timer.Tick += (s, e) => LoadProcesses();
            _timer.Start();
        }

        // Загружает список всех процессов и обновляет DataGrid
        // Также сохраняет выбранный процесс при обновлении
        private void LoadProcesses()
        {
            // Запоминаем выбранный PID и маску
            int? selectedPid = _selectedProcess?.Id;
            IntPtr savedMask = _selectedProcess?.ProcessorAffinity ?? IntPtr.Zero;
            bool[] savedCores = _coreCheckBoxes?.Select(cb => cb.IsChecked == true).ToArray();

            try
            {
                var processes = _service.GetAllProcesses();

                dgProcesses.ItemsSource = processes;
                tbCount.Text = processes.Count.ToString();

                // Восстанавливаем выделение по PID
                if (selectedPid.HasValue)
                {
                    var newSelected = processes.FirstOrDefault(p => p.Id == selectedPid.Value);
                    if (newSelected != null)
                    {
                        dgProcesses.SelectedItem = newSelected;
                        dgProcesses.ScrollIntoView(newSelected);
                        _selectedProcess = newSelected;

                        // Восстанавливаем маску и чекбоксы
                        newSelected.ProcessorAffinity = savedMask;
                        newSelected.AffinityMaskString = AffinityHelper.GetMaskAsString(savedMask, newSelected.CoreCount);

                        // Если были чекбоксы — восстанавливаем их галочки
                        if (savedCores != null && savedCores.Length == newSelected.CoreCount)
                        {
                            for (int i = 0; i < savedCores.Length; i++)
                            {
                                if (_coreCheckBoxes != null && i < _coreCheckBoxes.Length)
                                    _coreCheckBoxes[i].IsChecked = savedCores[i];
                            }
                        }
                    }
                }

                // Сортировка
                var view = CollectionViewSource.GetDefaultView(dgProcesses.ItemsSource);
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new System.ComponentModel.SortDescription("Name", System.ComponentModel.ListSortDirection.Ascending));

                LoadProcessTree();  // дерево тоже обновляем
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка обновления:\n" + ex.Message);
            }
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadProcesses();
        }

        // Когда выбран процесс в таблице
        private void dgProcesses_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgProcesses.SelectedItem is ProcessInfo selected)
            {
                _selectedProcess = selected;
                // Восстанавливаем чекбоксы, если они были сохранены
                if (_selectedProcess.SavedCoreSelection != null && _selectedProcess.SavedCoreSelection.Length == _selectedProcess.CoreCount)
                {
                    for (int i = 0; i < _coreCheckBoxes.Length; i++)
                    {
                        if (i < _selectedProcess.SavedCoreSelection.Length)
                            _coreCheckBoxes[i].IsChecked = _selectedProcess.SavedCoreSelection[i];
                    }
                }

                tbSelectedInfo.Text =
                    $"PID: {selected.Id}\n" +
                    $"Имя: {selected.Name}\n" +
                    $"Текущий приоритет: {selected.PriorityString}\n" +
                    $"Память: {selected.MemoryUsage} МБ\n" +
                    $"Потоков: {selected.ThreadCount}\n" +
                    $"Время CPU: {selected.CpuTime:hh\\:mm\\:ss}";

                cbPriority.SelectedValue = selected.Priority;
                UpdateAffinityUI(selected);
                LoadThreads(selected.Id);
            }
            else
            {
                _selectedProcess = null;
                tbSelectedInfo.Text = "Выберите процесс";
                cbPriority.SelectedValue = null;
                spCores.Children.Clear();
                dgThreads.ItemsSource = null;
            }
        }

        // Изменение приоритета выбранного процесса
        // Содержит проверку на RealTime и обработку ошибок доступа
        private void btnSetPriority_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedProcess == null || !(cbPriority.SelectedValue is ProcessPriorityClass newPriority))
            {
                MessageBox.Show("Выберите процесс и приоритет");
                return;
            }

            if (newPriority == ProcessPriorityClass.RealTime)
            {
                var res = MessageBox.Show("Приоритет RealTime может нарушить систему. Продолжить?",
                    "Внимание", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (res == MessageBoxResult.No) return;
            }

            if (_service.SetPriority(_selectedProcess.Id, newPriority))
            {
                _selectedProcess.Priority = newPriority;
                MessageBox.Show("Приоритет изменён");
                tbSelectedInfo.Text = tbSelectedInfo.Text.Replace(
                    $"Текущий приоритет: {_selectedProcess.PriorityString}",
                    $"Текущий приоритет: {newPriority}");
            }
        }

        // Обновление панели Affinity
        private void UpdateAffinityUI(ProcessInfo selected)
        {
            tbAffinityInfo.Text = $"Текущая маска: {selected.AffinityMaskString}";

            spCores.Children.Clear();
            int count = selected.CoreCount;
            _coreCheckBoxes = new CheckBox[count];

            for (int i = 0; i < count; i++)
            {
                var cb = new CheckBox
                {
                    Content = $"Ядро {i}",
                    Margin = new Thickness(0, 0, 15, 0),
                    IsChecked = AffinityHelper.IsCoreEnabled(selected.ProcessorAffinity, i)
                };
                _coreCheckBoxes[i] = cb;
                spCores.Children.Add(cb);
            }
            _selectedProcess.SavedAffinity = _selectedProcess.ProcessorAffinity;
            _selectedProcess.SavedCoreSelection = _coreCheckBoxes.Select(cb => cb.IsChecked == true).ToArray();
        }

        // Применение привязки процесса к выбранным ядрам CPU (Affinity)
        // Преобразует состояние чекбоксов в битовую маску и применяет её
        private void btnApplyAffinity_Click(object sender, RoutedEventArgs e)
        {
            // Проверяем, что процесс выбран и чекбоксы созданы
            if (_selectedProcess == null || _coreCheckBoxes == null)
            {
                MessageBox.Show("Сначала выберите процесс");
                return;
            }

            // Собираем массив галочек (какие ядра выбраны)
            bool[] selectedCores = new bool[_coreCheckBoxes.Length];
            for (int i = 0; i < _coreCheckBoxes.Length; i++)
            {
                selectedCores[i] = _coreCheckBoxes[i].IsChecked == true;
            }

            // Здесь создаём переменную newMask — она ОБЯЗАТЕЛЬНО должна быть именно здесь
            IntPtr newMask = AffinityHelper.SetCoreMask(selectedCores);

            // Применяем маску через сервис
            bool success = _service.SetProcessorAffinity(_selectedProcess.Id, newMask);  // или SetAffinity — как у тебя названо

            if (success)
            {
                MessageBox.Show("CPU Affinity успешно изменён!");

                // Обновляем данные в модели (чтобы при следующем обновлении не слетело)
                _selectedProcess.ProcessorAffinity = newMask;
                _selectedProcess.AffinityMaskString = AffinityHelper.GetMaskAsString(newMask, _selectedProcess.CoreCount);

                // Обновляем текст на экране
                tbAffinityInfo.Text = $"Текущая маска: {_selectedProcess.AffinityMaskString}";
            }
        }

        // Загружает и отображает список потоков выбранного процесса
        // Показывает ID потока, приоритет, состояние и время CPU
        private void LoadThreads(int pid)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                var list = new List<object>();

                foreach (ProcessThread t in proc.Threads)
                {
                    list.Add(new
                    {
                        Id = t.Id,
                        Priority = t.PriorityLevel.ToString(),
                        State = t.ThreadState.ToString(),
                        CpuTime = t.TotalProcessorTime
                    });
                }

                dgThreads.ItemsSource = list;
            }
            catch
            {
                dgThreads.ItemsSource = null;
            }
        }

        // Построение дерева иерархии процессов
        // Использует WMI для получения ParentProcessId и рекурсивно заполняет TreeView
        private void LoadProcessTree()
        {
            tvProcessTree.Items.Clear();

            var all = _service.GetAllProcesses();
            var dict = all.ToDictionary(p => p.Id, p => p);

            foreach (var p in all)
            {
                p.ParentId = ProcessParentHelper.GetParentProcessId(p.Id);
                if (p.ParentId > 0 && dict.ContainsKey(p.ParentId))
                    dict[p.ParentId].Children.Add(p);
            }

            var roots = all.Where(p => p.ParentId <= 0 || !dict.ContainsKey(p.ParentId)).ToList();

            foreach (var root in roots.OrderBy(p => p.Name))
            {
                tvProcessTree.Items.Add(CreateTreeItem(root));
            }
        }

        private TreeViewItem CreateTreeItem(ProcessInfo proc)
        {
            var item = new TreeViewItem
            {
                Header = $"{proc.Name} (PID: {proc.Id})",
                Tag = proc
            };

            foreach (var child in proc.Children.OrderBy(c => c.Name))
            {
                item.Items.Add(CreateTreeItem(child));
            }

            item.Selected += (s, e) =>
            {
                if (item.Tag is ProcessInfo sel)
                {
                    dgProcesses.SelectedItem = sel;
                    tbSelectedInfo.Text =
                        $"PID: {sel.Id}\n" +
                        $"Имя: {sel.Name}\n" +
                        $"Текущий приоритет: {sel.PriorityString}\n" +
                        $"Память: {sel.MemoryUsage} МБ\n" +
                        $"Потоков: {sel.ThreadCount}\n" +
                        $"Время CPU: {sel.CpuTime:hh\\:mm\\:ss}";

                    cbPriority.SelectedValue = sel.Priority;
                    UpdateAffinityUI(sel);
                    LoadThreads(sel.Id);
                }
            };

            return item;
        }
    }
}