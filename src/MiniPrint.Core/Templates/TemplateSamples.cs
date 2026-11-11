using MiniPrint.Core.Configuration;

namespace MiniPrint.Core.Templates;

/// <summary>
/// Modelos de exemplo prontos para uso. Servem como ponto de partida; o usuário
/// pode editar, duplicar ou criar modelos completamente diferentes.
/// </summary>
public static class TemplateSamples
{
    /// <summary>Comprovante de pagamento conforme o modelo sugerido.</summary>
    public static DocumentTemplate CreatePaymentReceipt()
    {
        var template = new DocumentTemplate
        {
            Name = "Comprovante de Pagamento",
            Description = "Modelo simples para registrar pagamentos recebidos.",
            FontSize = 11,
            Alignment = TextAlignment.Esquerda,
            LineSpacing = 1.25,
            MarginLeft = 10,
            MarginRight = 10,
            MarginTop = 8,
            MarginBottom = 10,
        };

        template.Fields.AddRange(new[]
        {
            new TemplateField
            {
                Key = "nome_pagador",
                Label = "Nome do pagador",
                Type = TemplateFieldType.TextoCurto,
                Placeholder = "Nome completo",
                Required = true,
            },
            new TemplateField
            {
                Key = "documento",
                Label = "CPF/CNPJ",
                Type = TemplateFieldType.TextoCurto,
                Placeholder = "000.000.000-00 ou 00.000.000/0000-00",
                Required = true,
            },
            new TemplateField
            {
                Key = "valor",
                Label = "Valor",
                Type = TemplateFieldType.Monetario,
                Placeholder = "150,00",
                Required = true,
            },
            new TemplateField
            {
                Key = "descricao",
                Label = "Referente a",
                Type = TemplateFieldType.TextoLongo,
                Placeholder = "Descrição do pagamento",
            },
            new TemplateField
            {
                Key = "forma_pagamento",
                Label = "Forma de pagamento",
                Type = TemplateFieldType.Lista,
                Required = true,
                Options = new List<string>
                {
                    "Dinheiro", "PIX", "Cartão de débito", "Cartão de crédito",
                    "Transferência", "Cheque",
                },
            },
            new TemplateField
            {
                Key = "data_pagamento",
                Label = "Data do pagamento",
                Type = TemplateFieldType.Data,
                Required = true,
            },
        });

        template.Blocks.AddRange(new[]
        {
            Paragraph("COMPROVANTE DE PAGAMENTO", bold: true, align: TextAlignment.Centro, size: 13),
            Divider(DividerStyle.Igual),
            Paragraph("Recebemos de: {{nome_pagador}}", spacingBefore: 6),
            Paragraph("CPF/CNPJ: {{documento}}"),
            Paragraph("A importância de: R$ {{valor}}"),
            Paragraph("Referente a: {{descricao}}"),
            Paragraph("Forma de pagamento: {{forma_pagamento}}"),
            Paragraph("Data: {{data_pagamento}}"),
            Divider(DividerStyle.Traco, spacingBefore: 6),
            Paragraph("PAGAMENTO RECEBIDO", bold: true, align: TextAlignment.Centro),
            Divider(DividerStyle.Traco),
            Paragraph("Assinatura: ____________________", spacingBefore: 10),
            Paragraph("Obrigado pela preferência!", align: TextAlignment.Centro, spacingBefore: 8),
            Divider(DividerStyle.Igual, spacingBefore: 6),
        });

        return template;
    }

    /// <summary>Recibo curto que demonstra CPF, telefone, valor e data.</summary>
    public static DocumentTemplate CreateServiceReceipt()
    {
        var template = new DocumentTemplate
        {
            Name = "Recibo de Serviço",
            Description = "Recibo rápido com dados do cliente, serviço e valor.",
            FontSize = 11,
            LineSpacing = 1.25,
            MarginLeft = 10,
            MarginRight = 10,
            MarginTop = 8,
            MarginBottom = 10,
        };

        template.Fields.AddRange(new[]
        {
            new TemplateField
            {
                Key = "cliente",
                Label = "Cliente",
                Type = TemplateFieldType.TextoCurto,
                Required = true,
            },
            new TemplateField
            {
                Key = "cpf",
                Label = "CPF",
                Type = TemplateFieldType.Cpf,
                Placeholder = "000.000.000-00",
            },
            new TemplateField
            {
                Key = "telefone",
                Label = "Telefone",
                Type = TemplateFieldType.Telefone,
                Placeholder = "(00) 00000-0000",
            },
            new TemplateField
            {
                Key = "servico",
                Label = "Serviço prestado",
                Type = TemplateFieldType.TextoLongo,
                Required = true,
            },
            new TemplateField
            {
                Key = "valor",
                Label = "Valor",
                Type = TemplateFieldType.Monetario,
                Required = true,
            },
            new TemplateField
            {
                Key = "data",
                Label = "Data",
                Type = TemplateFieldType.Data,
                Required = true,
            },
        });

        template.Blocks.AddRange(new[]
        {
            Paragraph("RECIBO", bold: true, align: TextAlignment.Centro, size: 14),
            Divider(DividerStyle.Traco),
            Paragraph("Recebi de: {{cliente}}", spacingBefore: 6),
            Paragraph("CPF: {{cpf}}"),
            Paragraph("Telefone: {{telefone}}"),
            Paragraph("Referente a: {{servico}}"),
            Paragraph("Valor: R$ {{valor}}"),
            Paragraph("Data: {{data}}"),
            Divider(DividerStyle.Traco, spacingBefore: 8),
            Paragraph("____________________________", align: TextAlignment.Centro, spacingBefore: 14),
            Paragraph("Assinatura", align: TextAlignment.Centro),
        });

        return template;
    }

    /// <summary>Etiqueta de produto com código e preço.</summary>
    public static DocumentTemplate CreateProductLabel()
    {
        var template = new DocumentTemplate
        {
            Name = "Etiqueta de Produto",
            Description = "Etiqueta compacta com nome, código e preço.",
            FontSize = 11,
            LineSpacing = 1.2,
            MarginLeft = 10,
            MarginRight = 10,
            MarginTop = 8,
            MarginBottom = 10,
        };

        template.Fields.AddRange(new[]
        {
            new TemplateField
            {
                Key = "produto",
                Label = "Produto",
                Type = TemplateFieldType.TextoCurto,
                Required = true,
            },
            new TemplateField
            {
                Key = "codigo",
                Label = "Código",
                Type = TemplateFieldType.TextoCurto,
                Placeholder = "COD-0000",
                Required = true,
            },
            new TemplateField
            {
                Key = "preco",
                Label = "Preço",
                Type = TemplateFieldType.Monetario,
                Required = true,
            },
            new TemplateField
            {
                Key = "descricao_produto",
                Label = "Descrição",
                Type = TemplateFieldType.TextoLongo,
            },
        });

        template.Blocks.AddRange(new[]
        {
            Paragraph("ETIQUETA", bold: true, align: TextAlignment.Centro, size: 12),
            Divider(DividerStyle.Igual),
            Paragraph("{{produto}}", bold: true, size: 12, spacingBefore: 4),
            Paragraph("Código: {{codigo}}", spacingBefore: 4),
            Paragraph("Preço: R$ {{preco}}", bold: true, size: 12),
            Paragraph("{{descricao_produto}}", spacingBefore: 4),
            Divider(DividerStyle.Traco, spacingBefore: 4),
        });

        return template;
    }

    /// <summary>Ordem de serviço com cliente, telefone e valor.</summary>
    public static DocumentTemplate CreateServiceOrder()
    {
        var template = new DocumentTemplate
        {
            Name = "Ordem de Serviço",
            Description = "Registro de serviço com número, cliente e valor.",
            FontSize = 11,
            LineSpacing = 1.25,
            MarginLeft = 10,
            MarginRight = 10,
            MarginTop = 8,
            MarginBottom = 10,
        };

        template.Fields.AddRange(new[]
        {
            new TemplateField
            {
                Key = "numero_os",
                Label = "Número da OS",
                Type = TemplateFieldType.TextoCurto,
                Placeholder = "OS-0001",
                Required = true,
            },
            new TemplateField
            {
                Key = "cliente_os",
                Label = "Cliente",
                Type = TemplateFieldType.TextoCurto,
                Required = true,
            },
            new TemplateField
            {
                Key = "telefone_os",
                Label = "Telefone",
                Type = TemplateFieldType.Telefone,
            },
            new TemplateField
            {
                Key = "servico",
                Label = "Descrição do serviço",
                Type = TemplateFieldType.TextoLongo,
                Required = true,
            },
            new TemplateField
            {
                Key = "valor_os",
                Label = "Valor",
                Type = TemplateFieldType.Monetario,
                Required = true,
            },
            new TemplateField
            {
                Key = "data_os",
                Label = "Data",
                Type = TemplateFieldType.Data,
                Required = true,
            },
        });

        template.Blocks.AddRange(new[]
        {
            Paragraph("ORDEM DE SERVIÇO", bold: true, align: TextAlignment.Centro, size: 12),
            Divider(DividerStyle.Igual),
            Paragraph("Nº: {{numero_os}}", bold: true, spacingBefore: 4),
            Paragraph("Cliente: {{cliente_os}}"),
            Paragraph("Telefone: {{telefone_os}}"),
            Paragraph("Data: {{data_os}}"),
            Divider(DividerStyle.Traco, spacingBefore: 6),
            Paragraph("Serviço:", bold: true),
            Paragraph("{{servico}}"),
            Divider(DividerStyle.Traco, spacingBefore: 6),
            Paragraph("Valor: R$ {{valor_os}}", bold: true, size: 12),
            Paragraph("____________________________", align: TextAlignment.Centro, spacingBefore: 14),
            Paragraph("Assinatura do cliente", align: TextAlignment.Centro),
        });

        return template;
    }

    public static IReadOnlyList<DocumentTemplate> All() => new[]
    {
        CreatePaymentReceipt(),
        CreateServiceReceipt(),
        CreateProductLabel(),
        CreateServiceOrder(),
    };

    private static TemplateBlock Paragraph(
        string text,
        bool bold = false,
        TextAlignment? align = null,
        int size = 0,
        int spacingBefore = 0,
        int spacingAfter = 0) => new()
    {
        Kind = TemplateBlockKind.Texto,
        Text = text,
        Bold = bold,
        Alignment = align,
        FontSize = size,
        SpacingBefore = spacingBefore,
        SpacingAfter = spacingAfter,
    };

    private static TemplateBlock Divider(DividerStyle style = DividerStyle.Traco, int spacingBefore = 0) => new()
    {
        Kind = TemplateBlockKind.Divisor,
        DividerStyle = style,
        SpacingBefore = spacingBefore,
    };
}
