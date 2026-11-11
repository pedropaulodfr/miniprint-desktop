using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using MiniPrint.Core.Models;
using MiniPrint.Core.Templates;
using MiniPrint.Desktop.Editing;
using MiniPrint.Imaging;
using Microsoft.Win32;

namespace MiniPrint.Desktop.ViewModels;

/// <summary>
/// Ponte entre as telas da aba Modelos. A lista, o editor e o preenchimento
/// são ViewModels independentes que se comunicam por esta interface.
/// </summary>
public interface ITemplateHost
{
    AppServices Services { get; }

    IReadOnlyList<DocumentTemplate> Templates { get; }

    void ShowTemplateList();

    void EditTemplate(DocumentTemplate template, bool isNew);

    /// <summary>Cria um documento em branco no editor visual.</summary>
    void NewVisualTemplate();

    void UseTemplate(DocumentTemplate template);

    void SaveTemplate(DocumentTemplate template, bool isNew);

    void DeleteTemplate(DocumentTemplate template);

    void DuplicateTemplate(DocumentTemplate template);

    void AddSample(DocumentTemplate sample);

    DocumentTemplate? ImportFromFile(string path);

    void ExportToFile(DocumentTemplate template);

    void RefreshTemplateList();
}

/// <summary>Aba Modelos: mantém os modelos e controla qual tela está visível.</summary>
public sealed class TemplatesViewModel : ViewModelBase, ITemplateHost
{
    private readonly ITemplateStore _store;
    private readonly List<DocumentTemplate> _templates = new();
    private object _current;
    private string _statusMessage = string.Empty;

    public TemplatesViewModel(AppServices services, ITemplateStore? store = null)
    {
        Services = services;
        _store = store ?? new JsonTemplateStore();

        LoadFromStore();

        List = new TemplateListViewModel(this);
        _current = List;
    }

    public AppServices Services { get; }

    public TemplateListViewModel List { get; }

    public IReadOnlyList<DocumentTemplate> Templates => _templates;

    /// <summary>ViewModel da tela atual (lista, editor ou preenchimento).</summary>
    public object Current
    {
        get => _current;
        private set => SetProperty(ref _current, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool IsShowingList => ReferenceEquals(Current, List);

    public void ShowTemplateList()
    {
        List.Reload();
        Current = List;
        OnPropertyChanged(nameof(IsShowingList));
    }

    public void EditTemplate(DocumentTemplate template, bool isNew)
    {
        template = TryRecoverDraft(template, isNew);

        object editor = template.IsVisual
            ? new VisualEditorViewModel(this, template, isNew)
            : new TemplateEditorViewModel(this, template, isNew);

        Current = editor;
        OnPropertyChanged(nameof(IsShowingList));
    }

    /// <summary>
    /// Se o editor foi fechado de forma inesperada, oferece o rascunho mais
    /// recente. O modelo salvo nunca é alterado sem o usuário aceitar.
    /// </summary>
    private static DocumentTemplate TryRecoverDraft(DocumentTemplate template, bool isNew)
    {
        if (isNew)
        {
            return template;
        }

        var draft = EditorDraftStore.TryLoad(template.Id);
        if (draft is null || draft.SavedAt <= template.UpdatedAt)
        {
            return template;
        }

        var answer = MiniPrint.Desktop.Windows.MessageDialog.Show(
            $"Existe um rascunho de \"{template.Name}\" gravado em {draft.SavedAt:dd/MM HH:mm}, " +
            "mais recente que a última versão salva." + Environment.NewLine + Environment.NewLine +
            "Deseja recuperar o rascunho?",
            "MiniPrint Desktop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer == MessageBoxResult.Yes)
        {
            return draft.Template;
        }

        EditorDraftStore.Delete(template.Id);
        return template;
    }

    /// <summary>Cria um documento em branco no editor visual.</summary>
    public void NewVisualTemplate()
    {
        var template = new DocumentTemplate
        {
            Name = "Novo documento",
            Description = string.Empty,
            PaperWidth = 384,
            Layout = new DocumentLayout
            {
                Width = 384,
                MarginLeft = 8,
                MarginRight = 8,
                MarginTop = 8,
                MarginBottom = 12,
                AutoHeight = true,
            },
        };

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Texto,
            Name = "Título",
            X = 8,
            Y = 8,
            Width = 368,
            Height = 22,
            Text = "TÍTULO DO DOCUMENTO",
            FontSize = 14,
            Bold = true,
            Alignment = Core.Configuration.TextAlignment.Centro,
        });

        template.Layout.Elements.Add(new LayoutElement
        {
            Kind = LayoutElementKind.Linha,
            Name = "Linha do cabeçalho",
            X = 8,
            Y = 32,
            Width = 368,
            Height = 3,
            Thickness = 2,
        });

        EditTemplate(template, isNew: true);
    }

    public void UseTemplate(DocumentTemplate template)
    {
        var fill = new TemplateFillViewModel(this, template);
        Current = fill;
        OnPropertyChanged(nameof(IsShowingList));
    }

    public void SaveTemplate(DocumentTemplate template, bool isNew)
    {
        template.UpdatedAt = DateTimeOffset.Now;

        var existingIndex = _templates.FindIndex(t => t.Id == template.Id);
        if (existingIndex >= 0)
        {
            _templates[existingIndex] = template;
        }
        else
        {
            template.CreatedAt = isNew ? DateTimeOffset.Now : template.CreatedAt;
            _templates.Add(template);
        }

        Persist();
        StatusMessage = isNew
            ? $"Modelo \"{template.Name}\" criado."
            : $"Modelo \"{template.Name}\" salvo.";

        ShowTemplateList();
        List.SelectById(template.Id);
    }

    public void DeleteTemplate(DocumentTemplate template)
    {
        _templates.RemoveAll(t => t.Id == template.Id);
        Persist();
        StatusMessage = $"Modelo \"{template.Name}\" excluído.";
        List.Reload();
    }

    public void DuplicateTemplate(DocumentTemplate template)
    {
        var copy = template.Clone(newIdentity: true);
        copy.Name = BuildCopyName(template.Name);
        copy.Description = string.IsNullOrWhiteSpace(template.Description)
            ? "Cópia de " + template.Name
            : template.Description;

        _templates.Add(copy);
        Persist();

        StatusMessage = $"Modelo duplicado como \"{copy.Name}\".";
        ShowTemplateList();
        List.SelectById(copy.Id);
    }

    public void RefreshTemplateList()
    {
        LoadFromStore();
        List.Reload();
    }

    /// <summary>Adiciona um modelo de exemplo pronto (não sobrescreve os existentes).</summary>
    public void AddSample(DocumentTemplate sample)
    {
        var copy = sample.Clone(newIdentity: true);
        copy.Name = BuildCopyName(sample.Name);
        _templates.Add(copy);
        Persist();

        StatusMessage = $"Modelo de exemplo \"{copy.Name}\" adicionado.";
        ShowTemplateList();
        List.SelectById(copy.Id);
    }

    /// <summary>Importa um modelo de um arquivo JSON validado.</summary>
    public DocumentTemplate? ImportFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var result = TemplateSerializer.Deserialize(json);

            if (!result.Success || result.Template is null)
            {
                MiniPrint.Desktop.Windows.MessageDialog.Show(
                    "O arquivo não pôde ser importado:" + Environment.NewLine + Environment.NewLine +
                    result.ErrorText,
                    "MiniPrint Desktop",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            var template = result.Template;
            template.Name = BuildCopyName(template.Name);
            _templates.Add(template);
            Persist();

            StatusMessage = $"Modelo \"{template.Name}\" importado.";
            ShowTemplateList();
            List.SelectById(template.Id);
            return template;
        }
        catch (Exception ex)
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                $"Falha ao ler o arquivo: {ex.Message}",
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return null;
        }
    }

    public void ExportToFile(DocumentTemplate template)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Exportar modelo",
            Filter = "Modelo do MiniPrint (*.json)|*.json",
            FileName = TemplateFieldFormatter.SuggestKey(template.Name) + ".json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, TemplateSerializer.Serialize(template));
            StatusMessage = $"Modelo exportado em {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MiniPrint.Desktop.Windows.MessageDialog.Show(
                $"Falha ao exportar: {ex.Message}",
                "MiniPrint Desktop",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void LoadFromStore()
    {
        _templates.Clear();
        try
        {
            _templates.AddRange(_store.Load());
        }
        catch (Exception ex)
        {
            Services.Log.Error("Modelos", $"Falha ao carregar modelos: {ex.Message}");
        }
    }

    private void Persist()
    {
        try
        {
            _store.Save(_templates);
        }
        catch (Exception ex)
        {
            Services.Log.Error("Modelos", $"Falha ao salvar modelos: {ex.Message}");
            StatusMessage = $"Não foi possível salvar os modelos: {ex.Message}";
        }
    }

    private string BuildCopyName(string name)
    {
        var baseName = string.IsNullOrWhiteSpace(name) ? "Modelo" : name.Trim();
        if (!_templates.Any(t => string.Equals(t.Name, baseName, StringComparison.OrdinalIgnoreCase)))
        {
            return baseName;
        }

        for (var i = 2; i < 100; i++)
        {
            var candidate = $"{baseName} ({i})";
            if (!_templates.Any(t => string.Equals(t.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }

        return baseName + " " + Guid.NewGuid().ToString("N")[..4];
    }
}

public enum TemplateSortMode
{
    NomeAscendente,
    AtualizacaoRecente,
    CriacaoRecente,
}

/// <summary>Listagem de modelos com pesquisa, ordenação e ações.</summary>
public sealed class TemplateListViewModel : ViewModelBase
{
    private readonly ITemplateHost _host;
    private string _searchText = string.Empty;
    private TemplateSortMode _sortMode = TemplateSortMode.AtualizacaoRecente;
    private DocumentTemplate? _selected;
    private string _statusMessage = string.Empty;

    public TemplateListViewModel(ITemplateHost host)
    {
        _host = host;

        NewCommand = new RelayCommand(_ => _host.NewVisualTemplate());
        NewFlowCommand = new RelayCommand(_ => NewFlowTemplate());
        UseCommand = new RelayCommand(p => UseTemplate(p as DocumentTemplate ?? Selected));
        EditCommand = new RelayCommand(p => EditTemplate(p as DocumentTemplate ?? Selected));
        DuplicateCommand = new RelayCommand(p => DuplicateTemplate(p as DocumentTemplate ?? Selected));
        DeleteCommand = new RelayCommand(p => DeleteTemplate(p as DocumentTemplate ?? Selected));
        ExportCommand = new RelayCommand(p => ExportTemplate(p as DocumentTemplate ?? Selected));
        ImportCommand = new RelayCommand(_ => ImportTemplate());
        AddSampleCommand = new RelayCommand(_ => AddSample());

        Reload();
    }

    public ObservableCollection<DocumentTemplate> Items { get; } = new();

    public IReadOnlyList<TemplateSortMode> SortModes { get; } = new[]
    {
        TemplateSortMode.NomeAscendente,
        TemplateSortMode.AtualizacaoRecente,
        TemplateSortMode.CriacaoRecente,
    };

    public RelayCommand NewCommand { get; }

    public RelayCommand NewFlowCommand { get; }

    public RelayCommand UseCommand { get; }

    public RelayCommand EditCommand { get; }

    public RelayCommand DuplicateCommand { get; }

    public RelayCommand DeleteCommand { get; }

    public RelayCommand ExportCommand { get; }

    public RelayCommand ImportCommand { get; }

    public RelayCommand AddSampleCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                Reload();
            }
        }
    }

    public TemplateSortMode SortMode
    {
        get => _sortMode;
        set
        {
            if (SetProperty(ref _sortMode, value))
            {
                Reload();
            }
        }
    }

    public DocumentTemplate? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                RaiseCommandStates();
            }
        }
    }

    public bool IsEmpty => Items.Count == 0;

    public string EmptyMessage => _host.Templates.Count == 0
        ? "Nenhum modelo cadastrado ainda. Crie um novo ou use um modelo de exemplo."
        : "Nenhum modelo corresponde à pesquisa.";

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public int Count => Items.Count;

    public void Reload()
    {
        var previousId = Selected?.Id;

        var query = _host.Templates.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            query = query.Where(t =>
                t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        query = SortMode switch
        {
            TemplateSortMode.NomeAscendente =>
                query.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase),
            TemplateSortMode.CriacaoRecente =>
                query.OrderByDescending(t => t.CreatedAt),
            _ => query.OrderByDescending(t => t.UpdatedAt),
        };

        Items.Clear();
        foreach (var template in query)
        {
            Items.Add(template);
        }

        Selected = previousId is { } id
            ? Items.FirstOrDefault(t => t.Id == id)
            : Items.FirstOrDefault();

        StatusMessage = Items.Count == 0
            ? EmptyMessage
            : $"{Items.Count} modelo(s).";

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(EmptyMessage));
        RaiseCommandStates();
    }

    public void SelectById(Guid id)
    {
        var match = Items.FirstOrDefault(t => t.Id == id);
        if (match is not null)
        {
            Selected = match;
        }
    }

    private void RaiseCommandStates()
    {
        // Os botões de cada item passam o modelo como parâmetro; os da barra
        // superior usam a seleção atual.
    }

    private void NewFlowTemplate()
    {
        var template = new DocumentTemplate
        {
            Name = "Novo modelo",
            Description = string.Empty,
            FontSize = 11,
            LineSpacing = 1.25,
            MarginLeft = 10,
            MarginRight = 10,
            MarginTop = 8,
            MarginBottom = 10,
        };

        template.Blocks.Add(new TemplateBlock
        {
            Kind = TemplateBlockKind.Texto,
            Text = "Digite aqui o texto fixo do documento.",
        });

        _host.EditTemplate(template, isNew: true);
    }

    private void UseTemplate(DocumentTemplate? template)
    {
        if (template is not null)
        {
            _host.UseTemplate(template);
        }
    }

    private void EditTemplate(DocumentTemplate? template)
    {
        if (template is not null)
        {
            _host.EditTemplate(template.Clone(), isNew: false);
        }
    }

    private void DuplicateTemplate(DocumentTemplate? template)
    {
        if (template is not null)
        {
            _host.DuplicateTemplate(template);
        }
    }

    private void DeleteTemplate(DocumentTemplate? template)
    {
        if (template is null)
        {
            return;
        }

        var confirm = MiniPrint.Desktop.Windows.MessageDialog.Show(
            $"Excluir o modelo \"{template.Name}\"?" + Environment.NewLine + Environment.NewLine +
            "Esta ação não pode ser desfeita.",
            "MiniPrint Desktop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _host.DeleteTemplate(template);
        }
    }

    private void ExportTemplate(DocumentTemplate? template)
    {
        if (template is not null)
        {
            _host.ExportToFile(template);
        }
    }

    private void ImportTemplate()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importar modelo",
            Filter = "Modelo do MiniPrint (*.json)|*.json|Todos os arquivos (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == true)
        {
            _host.ImportFromFile(dialog.FileName);
        }
    }

    private void AddSample()
    {
        var sample = VisualTemplateSamples.Convert(TemplateSamples.CreatePaymentReceipt());
        _host.AddSample(sample);
    }
}
