---
titulo: Catálogo de funcionalidades do MedlyCare
finalidade: a IA do Estúdio e das campanhas saber o que o sistema faz, em linguagem de usuário
origem: C:\SFA_Sistema apps/api/src/SFA.Api/Ia/Conhecimento/Fontes/funcionalidades.md (develop, 22/10/2026)
atualizado: 2026-11-19
confianca: alta (catálogo oficial do produto)
publico: equipe e IA; não citar texto literal em anúncio
aviso: versão v12; recursos depois dela (portal do paciente, IA v14) ainda faltam aqui
---

# MedlyCare: o que o sistema faz

_Atualizado em 16/10/2026 · versão v12 (a v11 já está em produção; a v12 entra no ar junto com este documento)_

## Agenda

- Agenda em lista ou calendário (dia, semana ou mês), com cores por tipo de consulta
- Agenda por profissional: cada um com seus dias e horários de atendimento, incluindo intervalo de almoço
- Bloqueio de períodos por férias, congresso, licença, feriado ou reunião. O sistema mostra na hora quem já tinha consulta marcada ali, com telefone e mensagem de remarcação pronta para o WhatsApp
- Painel "onde tem vaga?", que varre os próximos 30 dias e leva direto ao dia escolhido
- Consultas recorrentes, com opção de alterar só uma, todas ou as futuras
- Remarcar arrastando o horário no calendário, como no Google Agenda. Arrastar mantém o pacote de sessões, o "cobrar à parte", o valor combinado e o número da sessão avulsa: nada disso se perde no caminho
- Consulta que já foi atendida aparece com o texto riscado, para bater o olho e saber o que já saiu do dia
- Confirmação de consulta por WhatsApp: o paciente responde sim ou não pelo link
- Marcar falta, cancelar e reabrir um agendamento cancelado
- Na hora de marcar, já se escolhe de qual pacote de sessões aquela consulta sai, ou se ela é para cobrar à parte mesmo que o paciente tenha pacote em aberto
- Valor combinado na hora: quando a consulta foge da tabela de preço, o valor pode ser digitado ali mesmo, ao marcar ou ao encaixar na fila, e é ele que vale no fechamento
- Sessão numerada para quem atende em série, como na fisioterapia: a sessão avulsa recebe o número seguinte ao da última do paciente, sem ninguém precisar controlar isso à mão. Numa série recorrente, cada sessão recebe o seu número, em sequência
- O sistema recusa marcar o mesmo paciente com o mesmo profissional no mesmo horário, tanto ao criar quanto ao remarcar
- Dois pedidos para o mesmo horário do mesmo profissional, mesmo chegando no mesmo instante (recepção, portal ou vitrine), não viram duas marcações: o sistema segura um e avisa o outro
- A clínica decide se aceita marcar consulta com data que já passou, para quem precisa lançar o atendimento depois de feito
- Impressão da folha do dia com paciente, telefone, convênio e tipo de consulta
- Cadastrar um paciente novo sem sair da tela de agendamento
- Agendamento que o paciente fez sozinho pelo portal ou pela vitrine aparece na agenda com o selo "Online" e entra no mesmo fluxo de lembretes e confirmação dos demais

## Confirmações

- Central de confirmações: a fila de lembretes de consulta do dia numa tela só, com hora, paciente, telefone, profissional e o botão que abre o WhatsApp com a mensagem já pronta
- A fila é montada sozinha, todo dia, na hora e com a antecedência que a clínica escolher (de 1 a 14 dias), em Minha empresa
- Um clique abre o WhatsApp com a mensagem preenchida e registra quem enviou. "Enviar próximo" leva direto à consulta seguinte que ainda não foi avisada
- Consulta cancelada ou remarcada depois de entrar na fila some da lista sozinha
- A resposta do paciente (confirmou ou não vai poder comparecer) aparece na tela em menos de um minuto, e o mesmo ícone aparece no card da consulta na agenda
- Os contadores do topo funcionam como filtro: um clique mostra só aquele grupo, e uma barra mostra quantos lembretes do dia já foram processados
- A fila pode ser vista por período, de um a 31 dias, e o menu mostra quantos lembretes ainda faltam enviar hoje
- Admin e recepção veem a clínica inteira; cada profissional vê só as próprias consultas. Aparece para quem ligou os lembretes de consulta

### Lembrete por e-mail e segundo aviso

- Lembrete por e-mail automático, além do WhatsApp, para o paciente que tem e-mail cadastrado. Sai sozinho na mesma hora em que a fila é montada
- O e-mail traz os botões "Confirmar presença" e "Não poderei comparecer" (a mesma confirmação do WhatsApp) e "Adicionar ao calendário", que abre um evento pronto (arquivo .ics) com data, hora, local da clínica e alarme duas horas antes, além do link para o Google Agenda
- O remetente mostra o nome da clínica, e a resposta do paciente vai para a própria clínica, não para o MedlyCare
- Se o envio falha, o sistema tenta de novo, até três vezes. Paciente sem e-mail, ou clínica com o e-mail desligado, simplesmente não recebe: o WhatsApp continua sendo o canal principal
- Segundo aviso no dia anterior, opcional: quem recebeu o primeiro lembrete e ainda não respondeu ganha outro, e quem já respondeu não recebe

### Envio automático pela extensão do WhatsApp Web

- A clínica pode ligar o envio automático dos lembretes por um computador com o WhatsApp Web aberto, sem clicar em cada paciente da fila. A recepção liga em Minha empresa, depois de ler e aceitar o aviso de risco: o envio automatizado não é permitido pelos termos do WhatsApp e existe risco de bloqueio do número, por isso se recomenda um número secundário da clínica
- A extensão para Chrome e Edge é instalada uma vez no computador da recepção. O código de pareamento sai da Central, vale 10 minutos e serve uma vez. A publicação da extensão nas lojas do Chrome e do Edge ainda está pendente
- O painel da Central mostra se o envio está conectado, pausado ou sem contato, quantos lembretes já saíram hoje, o motivo de uma falha em texto simples e a lista de computadores pareados, cada um com a opção de revogar. O menu avisa quando a extensão está ligada e sem contato há mais de 30 minutos
- A recepção pode pausar e retomar os envios pela Central, e a pausa vale para todos os computadores pareados
- O envio respeita horário comercial (configurável por clínica), um limite de mensagens por hora e uma pausa entre envios; três falhas seguidas pausam o envio por 30 minutos
- Antes de mandar, a extensão confere se a conversa já termina com aquela mensagem, para não repetir o envio. Ela não lê conversas, contatos nem mídia do WhatsApp

## Agenda online e vitrine

- Agenda online: o paciente marca sozinho, pelo portal, nos horários que cada profissional abrir. A clínica liga o portal e a agenda online em Minha empresa; cada profissional liga a própria agenda em Meu perfil. O administrador só consegue desligar a agenda de alguém, nunca ligar
- Para abrir a agenda, o profissional precisa ter os horários de atendimento configurados e escolher ao menos um tipo de consulta. Ele define como o agendamento entra: direto, na hora, ou com a aprovação dele
- Os prazos são da clínica e cada profissional pode ser mais rígido: antecedência mínima (padrão de 2 horas), até quantos dias à frente os horários aparecem (padrão de 60) e até quantas horas antes o paciente pode cancelar sozinho (padrão de 24)
- Limites que protegem a agenda: quantas consultas futuras o mesmo paciente pode ter online com cada profissional e na clínica inteira
- Prévia no perfil do profissional: os horários que o paciente veria nos próximos 7 dias, já descontados expediente, bloqueios, horários ocupados e a antecedência mínima
- Agendamento com aprovação: o horário fica reservado como pedido, com cor própria na agenda, até a recepção, o administrador ou o profissional aprovar ou recusar, direto no sino ou no detalhe do agendamento. Recusar pede um motivo, e o paciente recebe só uma mensagem neutra. Se ninguém responde no prazo, o pedido expira sozinho e o paciente é avisado
- Cancelar e remarcar pelo portal, dentro da janela que a clínica definiu. O paciente escolhe um motivo de uma lista curta, a clínica recebe o aviso, e a remarcação só troca o horário se o novo estiver livre: se não estiver, o antigo continua valendo
- Bloqueio por faltas: o paciente que faltou o número de vezes definido pela clínica, dentro do período definido (padrão de 3 faltas em 180 dias), não agenda mais sozinho e é orientado a falar com a clínica. A recepção vê o bloqueio na ficha do paciente e libera com um motivo registrado
- Marcação, cancelamento e remarcação pelo portal geram um aviso no sino da equipe, e cada passo gera um e-mail ao paciente, com o convite para o calendário quando há horário novo. No atendimento de saúde mental, o e-mail não cita a especialidade nem o tipo de consulta
- Perfil público do profissional: nome de exibição, profissão, registro no conselho, especialidades (de uma lista fixa por conselho, com o RQE exigido para as especialidades médicas), apresentação de até 600 caracteres e foto opcional. Só o próprio profissional edita e publica, depois de aceitar um termo de responsabilidade, e vê a pré-visualização de como o paciente enxerga
- O sistema recusa na apresentação o que os conselhos proíbem na publicidade: preço, promessa de resultado, depoimento, antes e depois, link, e-mail, telefone e rede social
- Vitrine pública "Encontre um profissional", sem login: busca por especialidade, cidade ou nome, com o próximo horário livre de cada profissional e a página do perfil com os horários dos próximos dias, endereço e mapa. Só aparece quem publicou o perfil e está numa clínica que ligou a vitrine. Despublicar tira da busca na hora
- A clínica preenche os dados públicos que aparecem junto de cada profissional (nome de exibição, cidade, telefone opcional) e escolhe se o logotipo vai para a vitrine
- Agendar pela vitrine: o horário escolhido continua valendo enquanto a pessoa entra ou cria a conta ali mesmo, confirmando o e-mail com um código. Quem já é paciente da clínica confirma o vínculo por código; quem é novo aceita compartilhar os dados com a clínica, e o cadastro nasce junto com o agendamento
- A clínica limita quantos cadastros novos aceita por dia pela vitrine e pode exigir que a primeira consulta de paciente novo passe por aprovação
- Cadastro online: o paciente criado pela vitrine aparece com o selo "Paciente novo" e o filtro "Só cadastros online" lista todos
- Mesclagem de cadastros: quando o cadastro online parece ser de alguém que a clínica já tinha (mesmo CPF, ou mesmo nome e nascimento), a recepção recebe o aviso "Possível cadastro duplicado", compara os dois lado a lado e mescla ou mantém separados. A mesclagem junta agendamentos e atendimentos no cadastro antigo, mas não passa o acesso da conta ao portal: o paciente recebe um convite no e-mail do cadastro existente e só vê os dados depois de aceitar

## Portal do paciente

- Conta própria do paciente, só dele e independente de qualquer clínica, para maiores de 18 anos. Cadastro com CPF, data de nascimento, celular e e-mail, com confirmação por link no e-mail e aceite do Termo de Uso do Portal e da Política de Privacidade
- Entrada com segundo fator: em um navegador que o sistema não conhece, pede um código de 6 dígitos enviado ao e-mail, e o navegador fica lembrado por 30 dias. Recuperar a senha, trocar senha, e-mail e celular, sair de todos os aparelhos e excluir a conta ficam na própria conta
- Vínculo com a clínica por dois caminhos: o paciente busca a clínica e confirma com um código enviado ao e-mail que a clínica tem no cadastro dele, ou aceita o convite da recepção (por e-mail ou por link no WhatsApp, válido por 7 dias e de uso único, confirmando a data de nascimento). O vínculo nunca nasce só do CPF digitado
- A mesma conta pode estar vinculada a várias clínicas, e o paciente desfaz o vínculo quando quiser. A recepção também pode revogar o acesso, com motivo
- Meus dados: o que a clínica tem no cadastro dele, só para consulta, com o pedido "Solicitar correção" que chega à recepção como aviso
- Agendar consulta: escolhe a clínica, o profissional, o tipo, o dia e o horário, e recebe a confirmação com o convite para o calendário. Se o horário foi tomado no meio do caminho, o sistema oferece os próximos livres na hora
- Meus agendamentos: as consultas futuras e passadas em cada clínica vinculada, inclusive as que a recepção marcou, com data, hora, profissional, tipo, situação e endereço. Observações da clínica, valores e dados clínicos nunca aparecem aí. Dali o paciente cancela ou remarca
- Meus documentos: atestado, receita, laudo e solicitação de exames assinados, e os termos, para ver e baixar, com o link para validar a assinatura. A clínica decide se entrega documentos no portal e quais categorias ficam de fora, como psicologia. O paciente recebe um e-mail neutro quando há documento novo e pode desligar o aviso
- Pedido de acesso ao histórico clínico: o paciente pede para ver o que cada profissional registrou, e cada profissional responde pela sua parte em até 15 dias. O paciente acompanha a situação de cada parte e recebe e-mail a cada atualização, sem nome de profissional nem motivo
- Histórico liberado em leitura: o que o profissional liberou aparece em linha do tempo (consultas com fichas e imagens, anamnese, consultas do sistema anterior e arquivos), para ver ou baixar. A liberação acompanha os atendimentos futuros do profissional até alguém encerrá-la, e quem lê precisa confirmar a senha. Cada abertura fica registrada
- A cópia de histórico que o paciente baixa sai em PDF no formato que a clínica imprime, sem assinatura digital e com a marca "Cópia para o paciente"

## Atendimento

- Fila de espera para quem chega sem hora marcada
- Tela de atendimento com prontuário, histórico das últimas consultas à mão e cronômetro da consulta
- Gravação da consulta com transcrição automática, que acontece **enquanto o profissional fala**: ao encerrar a gravação, o texto já está praticamente pronto para revisar e inserir no prontuário, e o áudio não sai do computador dele
- Prontuário em texto corrido ou no formato **SOAP** (Subjetivo, Objetivo, Avaliação e Plano), à escolha de cada profissional, que define como os prontuários dele abrem por padrão
- Fotos e imagens no prontuário: por arquivo ou coladas direto da tela, e elas continuam lá quando o documento é impresso ou assinado
- Textos prontos ("orientações pós-operatórias", termo de consentimento) inseridos no prontuário com um clique, no ponto onde o cursor está, sem apagar o que já foi escrito
- Alertas da anamnese em destaque no topo da tela: alergia, uso de anticoagulante e o que mais a clínica marcar como alerta
- Ficha da sessão para quem trabalha com aplicação (estética, harmonização): o modelo traz a imagem de referência, como um mapa de musculatura ou um par de rostos, e o profissional marca direto na imagem, anotando quantidade e produto aplicado em cada ponto, com a soma por região calculada
- A marcação não é só ponto: dá para desenhar seta contínua, seta tracejada e barra, conforme a técnica usada
- Bloco de dados do produto na ficha: data da diluição, volume, número do lote, validade e data da aplicação, que já nasce com a data de hoje
- Cada modelo de ficha decide se o popup de cada marcação pergunta produto e quantidade ou só a quantidade, para quem trabalha com um produto só por sessão e não quer repetir a informação ponto a ponto. Ficha já preenchida não muda, e o produto gravado continua aparecendo
- A ficha é da sessão, não do paciente: a próxima aplicação gera outra ficha, e comparar uma com a outra é justamente o valor clínico
- Saldo do pacote de sessões à vista onde ele é preciso, no detalhe do agendamento, na fila de espera e na tela de atendimento, com aviso quando a série está chegando ao fim
- Descobriu no meio da consulta que aquela sessão não deve sair do pacote? Dá para desvincular ali mesmo e cobrar à parte
- Card flutuante que mantém o atendimento em aberto enquanto se navega por outras telas
- Na fisioterapia, um profissional pode assumir o atendimento de um colega da equipe

## Documentos

- Atestado, receituário, laudo e solicitação de exames
- Receituário de Controle Especial já nas duas vias e no formato exigido pela farmácia
- Autocomplete de medicamentos com a posologia que aquele profissional costuma prescrever, e modelos de receita para conjuntos recorrentes ("pós-operatório de joelho")
- Sugestão de medicamentos também pela lista oficial da ANVISA, com apresentação, laboratório e preço de referência. A receita já vem preenchida desde a primeira vez, antes de a clínica ter catálogo próprio
- Kits de exames montados por profissional, aplicados no pedido em um clique
- Termo de consentimento com modelo da própria clínica: o sistema preenche sozinho os dados da clínica (razão social, CNPJ, endereço, telefone) e do paciente (nome, CPF, data de nascimento), e cada termo emitido fica guardado na ficha do paciente e numa tela própria
- Editor de texto completo para os modelos de termo: fonte, tamanho, cor e realce, negrito, itálico, sublinhado, listas com subnível, títulos, tabelas com colunas ajustáveis, linha de assinatura, quebra de página, localizar e substituir, contador de palavras e tela cheia. Colar do Word ou do Google Docs mantém a formatação que importa
- O termo sai igual na tela, na impressão, no PDF enviado por WhatsApp e no anexo da anamnese e da ficha, na mesma fonte em qualquer computador. Antes, só a impressão respeitava a formatação
- Pré-visualização do termo em PDF, para o administrador conferir o resultado antes de usar. O PDF leva a marca "Apenas visualização, sem valor jurídico", e cada pré-visualização fica registrada
- O modelo de termo aceita anexos (folheto do procedimento, bula, mapa de cuidados, em PDF, JPEG ou PNG), que passam a sair junto para todo paciente que receber aquele termo
- Termo enviado ao paciente por WhatsApp, ou anexado à impressão da anamnese e da ficha para ele assinar de caneta na recepção
- Prontuário completo do paciente, com todo o histórico, para imprimir ou assinar
- Impressão de **uma consulta só**, pelo histórico do paciente: o que foi feito naquele dia, para entregar ao paciente, anexar a um processo ou levar a outro profissional, sem precisar do prontuário inteiro nem de assinatura
- Impressão da anamnese sozinha, e a opção de incluí-la no prontuário impresso: só a versão atual ou todas, em ordem
- Impressão da ficha da sessão, com ou sem assinatura digital
- As impressões são enxutas de propósito, para gastar menos papel: margens menores, tabelas compactas e o termo anexado seguindo no mesmo fluxo, sem abrir página nova
- Todos os documentos saem com o logotipo e os dados da clínica, e o que não é assinado diz isso no rodapé
- Envio do documento assinado ao paciente por WhatsApp

## Assinatura digital

- Assinatura com certificado digital ICP-Brasil (A1) do profissional responsável. Se ele não tem certificado próprio cadastrado, a clínica assina no lugar dele com o certificado único da empresa, o que serve para quem compra um certificado só para toda a equipe (certificado do profissional vencido não cai no da clínica: precisa ser renovado)
- Documento com QR Code de validação pública: quem recebe confere a autenticidade sem acessar o conteúdo clínico
- Registro de tudo que foi assinado (atestado, receituário, laudo, solicitação de exames, prontuário e ficha), com quem assinou, quando e o quê
- Documento assinado não se edita. Para corrigir, cancela e emite outro, do jeito que a norma exige

## Pacientes

- Cadastro completo com foto, endereço preenchido pelo CEP e convênio
- Campos demográficos opcionais (sexo, gênero, raça/cor, estado civil, escolaridade, ocupação, naturalidade, nome da mãe, responsável). Nenhum deles trava o cadastro de quem já estava na base
- Busca por nome, CPF, telefone ou e-mail, com ou sem pontuação
- Botão que mostra o próximo horário marcado do paciente e leva direto ao dia dele na agenda, mesmo que seja meses à frente
- Anamnese na ficha do paciente, preenchida a partir dos modelos da clínica. Atualizar nunca apaga a anterior: cria uma versão nova e o histórico fica inteiro, cada versão consultável do jeito que foi preenchida
- Dá para trocar o modelo da anamnese depois da primeira vez, quando o paciente muda de tratamento ou de especialidade
- Anamnese preenchida **pelo próprio paciente**, por um link enviado no WhatsApp antes da consulta: o link pede os últimos quatro dígitos do CPF, vale uma vez só, expira no prazo que a clínica definir, e fica registrado que quem preencheu foi o paciente
- Anamnese com validade: o modelo pode vencer em dias, meses ou anos, e o sistema mostra quando aquela ficha precisa ser refeita
- Classificação de complexidade do paciente, com regras próprias de agendamento
- Anexos (exames, documentos) no cadastro, com registro de quem acessou. O anexo de um profissional de saúde só aparece para quem pode ver o prontuário dele: o próprio autor, o grupo de fisioterapia, o administrador e quem recebeu o compartilhamento. O anexo da recepção é de toda a equipe, e a aba avisa quantos anexos de outros profissionais ficaram de fora para o seu perfil
- Arquivo que não é do tipo que diz ser (um executável com nome de PDF, por exemplo) é recusado no envio de anexo, de foto e de imagem do prontuário
- Histórico clínico completo, consulta a consulta, com tudo que foi emitido
- O histórico mostra o número da sessão de cada atendimento: sessão 3 de 10 do pacote, sessão avulsa, ou sessão estornada
- O conteúdo de cada atendimento, ficha, anamnese e prontuário completo segue uma regra só: cada profissional vê o que registrou, o grupo de fisioterapia vê o do grupo, o administrador vê tudo e a recepção nunca vê o texto do prontuário. A lista do histórico continua mostrando que o atendimento existiu, com a data e o profissional
- Compartilhamento do histórico entre profissionais mediante autorização. Quem recebeu lê, só para leitura, o que o outro profissional fez naquele paciente, até a revogação
- Aba "Portal do paciente" na ficha, para o administrador e a recepção: mostra se o paciente tem acesso, desde quando e por qual caminho, envia o convite por e-mail ou copia o link para mandar pelo WhatsApp, revoga o acesso com motivo e libera o paciente bloqueado por faltas
- Pedidos de acesso ao histórico: o profissional de saúde recebe, no menu Pedidos de acesso e no sino, o que o paciente pediu para ver da parte que ele registrou. Aprova, aprova escondendo itens específicos (com justificativa) ou recusa. A recusa só é aceita quando o acesso traria risco ao paciente ou a terceiros, com justificativa escrita, porque o paciente tem direito de acesso ao próprio prontuário. A recepção não vê esse menu
- O prazo legal é de 15 dias: o profissional é lembrado no quinto dia, os substitutos (administradores que também são profissionais de saúde) passam a ver o pedido no décimo, e no décimo quinto o pedido aparece vencido, com o paciente orientado a pedir a cópia direto à clínica. Nada é liberado automaticamente. Registro sem dono, ou de profissional que saiu, vai para os substitutos
- Liberações do portal na aba de histórico do paciente: o profissional vê o que está liberado, esconde ou volta a mostrar itens com justificativa, e revoga a liberação a qualquer momento, também com motivo
- Importação da base de pacientes por planilha, do Doctor's Office ou do Clínica Ágil. Deste último vêm também o histórico de sessões, as avaliações preenchidas e o financeiro em aberto, com prévia antes de confirmar e sem duplicar quem já está cadastrado

## Comunicação interna

- Chat entre as pessoas da clínica, recepção inclusive: "o paciente das 14h chegou" sem sair da tela
- Aviso de mensagem nova em poucos segundos, com a janela aberta ou fechada
- Conversa antiga é apagada sozinha no prazo que a clínica escolher (90 dias por padrão), porque guardar conversa sem motivo é passivo de LGPD. Quem preferir pode desligar o expurgo
- Central de notificações da equipe: um sino no topo do menu (no celular, o item Notificações do menu inferior) com contador, as últimas dez à vista e uma página com filtros por Agenda, Clínico, Cadastro e Pendentes. Cada pessoa marca como lida, limpa a própria lista e não mexe na dos colegas
- Dois tipos de item: o aviso, lido por pessoa (um paciente que cancelou, uma marcação nova pelo portal, um pedido de correção de cadastro), e a ação pendente, que some para todos quando alguém resolve e mostra quem resolveu e quando. Pedido de agendamento e possível cadastro duplicado só se resolvem pela própria decisão (aprovar, recusar, mesclar ou manter separado), nunca marcando como resolvido à mão
- Cada categoria chega só a quem deve: Agenda para o administrador, a recepção e quem enxerga aquela agenda; Clínico só para profissionais de saúde, nunca para a recepção; Cadastro para administrador e recepção
- O clique abre o item de origem, como o agendamento já na agenda. A página se atualiza sozinha a cada 30 segundos enquanto a aba está aberta, com um som discreto, diferente do chat, que cada pessoa pode desligar em Preferências. As notificações ficam guardadas por 90 dias

## Financeiro

- Contas a pagar e a receber, com categorias próprias da clínica, parcelamento, baixa por data e forma de pagamento, e o vencido sempre calculado pelo relógio da clínica
- Consulta marcada aparece como **previsão** de receita, calculada na hora: remarcou, cancelou ou trocou de profissional e a projeção se ajusta sozinha, sempre separada do que já foi lançado de verdade
- Atendimento finalizado vira conta a receber automaticamente, pelo valor que valia naquele momento
- Quem só quer ver o realizado desliga as previsões da tela, e o total previsto sai junto
- Lançamento errado tem conserto que preserva a história. **Cancelar** anula o que nunca vai acontecer; **estornar** desfaz um recebimento indevido e devolve a conta para em aberto. Motivo é obrigatório, e quem fez, quando e o que foi desfeito fica registrado
- Toda edição de valor, categoria, vencimento ou responsável deixa rastro, consultável a partir do próprio lançamento
- Importação do XML da nota fiscal eletrônica (NF-e) no contas a pagar: cada parcela da nota vira uma conta a pagar, com o vencimento e o valor que estão no arquivo, e a nota sem parcela vira uma conta só. Uma prévia mostra o que será lançado antes de gravar, e a mesma nota nunca entra duas vezes
- A aba Notas fiscais lista as notas importadas, com busca por número, emitente, CNPJ ou chave de acesso, filtro por período e o detalhe com itens, impostos e as parcelas geradas. Excluir uma nota cancela as contas ainda pendentes, e a exclusão é recusada se alguma parcela já foi paga
- Pacotes de sessões: catálogo da clínica (quantidade de sessões, valor, validade, tipo de consulta), venda ao paciente à vista ou parcelada, saldo calculado sessão a sessão e consumo automático ao finalizar o atendimento
- O saldo nunca trava o trabalho: pacote vencido, esgotado ou inexistente não impede marcar nem fechar a consulta
- A recepção enxerga o catálogo de pacotes, com o preço, para vender o pacote na ficha do paciente e vincular a sessão ao marcar a consulta. Criar, editar e inativar pacote continua só com o administrador
- Pacote que permite terceiros pode ser consumido por outro paciente, como a mãe que compra a série e divide com a filha, sempre por escolha explícita de quem finaliza
- Estornar uma sessão consumida e cancelar uma venda são possíveis. Parcela já recebida o sistema não mexe sozinho: ele avisa quanto já entrou, e quem decide é a clínica
- Tabela de preço por profissional e tipo de consulta, com um valor padrão para o que não tiver preço específico
- O valor vigente é gravado no atendimento no momento em que ele é finalizado, então mudar a tabela depois não altera o que já foi feito
- Painel de faturamento do mês, com comparativo do período anterior, e a clínica escolhe se o recorte é o mês corrente ou os últimos 30 dias
- Cada perfil enxerga o que lhe cabe: o administrador vê a clínica inteira, o profissional vê apenas os próprios recebimentos e não alcança as contas a pagar, e a recepção não acessa o financeiro
- Toda alteração de preço fica registrada, com autor, data, valor anterior e valor novo
- Clínica que ainda não cadastrou preços continua finalizando atendimentos normalmente

## Gestão e relatórios

- Painel inicial com próximos atendimentos e pacientes novos do mês
- Relatório de atendimentos com filtros por período, profissional, paciente e situação
- Impressão resumida por paciente, com os totais de cada situação e o resumo das sessões consumidas, para fechar o mês com um convênio ou conferir a série de um paciente
- Coluna de sessão no relatório, mostrando se o atendimento saiu de pacote, foi avulso ou teve a sessão estornada
- Backup dos dados da clínica em um arquivo único, para download, cobrindo agenda, financeiro, pacientes, prontuários e cadastros
- Registro de quem criou e de quem alterou cada paciente, usuário, agendamento e atendimento, com data e hora
- Registro de quem abriu o prontuário de cada paciente, cobrindo a consulta em curso, o histórico, a impressão e o download de documentos já assinados
- Registro das reimpressões de documento, das alterações feitas na agenda e do que aconteceu em cada atendimento
- O registro de acesso também cobre quem abriu ou imprimiu um documento clínico, quem consultou a linha do tempo e a lista de atendimentos de um paciente, e quem listou os atestados, laudos e solicitações de exame da clínica, uma linha por documento. A impressão e a abertura de documento guardam também o endereço de internet de quem acessou
- Exportar a base de pacientes e os dados da clínica deixa registro de quem exportou, quando e quantos registros. A planilha exportada é protegida contra fórmulas escondidas, que poderiam rodar ao abrir no Excel
- A fila de espera e os atendimentos em aberto deixam de mostrar o texto da evolução clínica, que nenhuma dessas telas precisava exibir
- Os registros de acesso e o aceite dos termos ficam guardados enquanto o prontuário existir, por no mínimo 20 anos, sem expurgo automático
- Os acessos do paciente pelo portal (listar, abrir e baixar documento ou histórico) ficam registrados com a conta, o endereço de internet e o navegador

## Configuração da clínica

- Dados da clínica, logotipo, certificado digital e duração padrão da consulta
- Cadastros próprios: tipos de consulta (com cor e duração), tipos de paciente, complexidades e regras de agendamento
- Modelos de anamnese e de ficha: o sistema já vem com um catálogo pronto, a clínica monta os seus e cada profissional monta os dele, com perguntas de texto, número, data, escolha, sim ou não, "não sei, não, sim (qual?)", tabela de aplicações por região do corpo e bloco de dados do produto
- Modelos de termo de consentimento, com as variáveis da clínica e do paciente que o sistema preenche na emissão
- Modelo próprio pode ser duplicado, para criar uma variação sem montar tudo de novo. A cópia leva junto a imagem de referência da ficha e os PDFs anexados ao termo, como arquivos só dela
- Compartilhar modelo de termo, anamnese e ficha: no formulário de cada modelo, o campo "Quem pode usar" aceita "Só eu", profissionais específicos (escolhidos na lista de usuários da clínica) ou "Toda a clínica", que alcança também quem for cadastrado depois. Quem recebe vê o modelo em "Compartilhados comigo", aplica, mas não edita, não exclui e não recompartilha: para mudar, duplica e fica com uma cópia só sua
- Compartilhar é dar acesso, não copiar: quando o dono edita, quem recebeu passa a usar a versão nova, e o que já foi emitido ou preenchido não muda. Parar de compartilhar tira o acesso na hora. Anamnese só vai para quem é profissional de saúde, e outra clínica nunca vê o modelo. O histórico de quem compartilhou, com quem e de quem parou fica registrado
- Cada profissional escolhe quais modelos aparecem para ele na hora de preencher, para não procurar a ficha no meio das de outra especialidade, e a escolha de um nunca muda o que os colegas veem
- Modelo excluído guarda quem excluiu e quando. O catálogo que vem com o sistema não se apaga
- Catálogo de pacotes de sessões e tabela de preço por profissional
- Usuários, perfis de acesso e permissões, com cada perfil vendo apenas o que lhe cabe
- A clínica nunca fica sem ninguém que a administre: o sistema recusa excluir ou rebaixar o último administrador, e não deixa apagar um perfil de acesso que ainda tenha gente usando
- Recursos que a clínica liga ou desliga quando quiser: agenda por profissional, chat, gravação da consulta, sessão avulsa numerada, falta consumindo sessão do pacote, consulta com data retroativa, prazo de expurgo do chat, lembretes de consulta, envio automático pela extensão, portal do paciente, agenda online e vitrine
- Portal do paciente em Minha empresa: vem desligado e sem custo. A clínica só aparece na busca de quem tem conta quando liga, e desligar encerra o acesso de todos os pacientes na hora, derrubando junto a agenda online e a vitrine. A agenda online e a vitrine exigem o portal ligado
- Em Minha empresa, a aba Agenda online reúne o interruptor da agenda online, o da vitrine, os padrões da clínica (antecedência, horizonte, cancelamento, faltas, limites e validade da reserva) e os dados públicos que aparecem na vitrine. Cada mudança fica registrada com quem fez, o que mudou e quando
- A clínica escolhe se entrega documentos no portal e quais categorias de profissional ficam fora dele, como psicologia, tanto nos documentos quanto no histórico liberado
- Termos de Uso, Política de Privacidade (LGPD) e Termo de Consentimento do paciente em páginas públicas, com versão e data de vigência. O aceite fica registrado por pessoa e por versão, e é pedido de novo quando o texto muda
- O paciente tem o próprio Termo de Uso do Portal, aceito no cadastro da conta. Os textos da v12 descrevem os papéis de cada parte: a clínica continua controladora do prontuário, do cadastro e da agenda, e o MedlyCare é controlador dos dados da conta do paciente, dos perfis públicos e do agendamento pela vitrine. Depois da atualização, a equipe, os pacientes e o profissional que for publicar o perfil aceitam os textos novos no primeiro acesso

## Segurança e privacidade

- Confirmar a senha de novo antes de ação sensível: passados 15 minutos desde a última confirmação, o sistema pede a senha para trocar senha, e-mail ou situação de um usuário, criar, ativar, inativar ou excluir usuário, atribuir perfil, editar perfis e permissões, mudar dados da empresa, da assinatura ou do certificado digital, mexer na agenda online e na vitrine da clínica e parear ou revogar o aparelho do WhatsApp. O login conta como confirmação, e o sistema refaz a ação sozinho depois da senha
- Entrada protegida: a mensagem de erro é a mesma para usuário que não existe e senha errada, e cinco tentativas erradas seguidas bloqueiam a entrada por um tempo que cresce (1, 5 e 15 minutos), com o tempo de espera na tela. A recuperação de senha tem verificação de segurança e responde igual exista ou não o e-mail, e senha fraca ou com menos de 8 caracteres é recusada
- A sessão da equipe se renova sozinha, expira depois de 12 horas sem uso ou 7 dias depois do login, e cada pessoa pode sair de todos os aparelhos de uma vez
- O paciente entra no portal com segundo fator em navegador novo, e as ações dele no histórico clínico pedem a senha de novo se a última confirmação passou de 15 minutos
- A foto do profissional e o logotipo da clínica só vão para a vitrine se a pessoa marcar, como cópia recortada e sem dados de localização nem da câmera. O original continua privado, e despublicar apaga a cópia
- O paciente que quiser uma cópia de todos os dados que a MedlyCare guarda dele pede por e-mail ao encarregado de dados, nomeado na Política de Privacidade

## No dia a dia

- Roteiro de primeiros passos dentro do próprio sistema: um "Comece por aqui" que acende o botão certo na tela e espera a pessoa usar, em vez de só descrever. Cada perfil recebe o roteiro que lhe cabe
- O passo a passo pode ser refeito quando quiser, para treinar alguém novo na equipe, sem criar paciente nem agendamento de teste
- Manual do sistema aberto na internet, dividido por módulo e com busca, para consultar na hora da dúvida ou mandar o link para quem entrou na equipe agora. Inclui o módulo do portal do paciente e da agenda online
- Funciona no computador e no celular, com instalação como aplicativo
- Aviso discreto quando sai uma versão nova, com lembrete a cada duas horas até a pessoa atualizar, nunca uma janela no meio do atendimento, e um botão na tela de login que força a versão mais recente
- Tema claro e escuro, e sistema disponível em português, inglês e espanhol
- Cada pessoa escolhe como sua agenda abre: lista ou calendário, dia, semana ou mês, e o profissional pode esconder os dias em que não atende
- Cadastro da clínica pelo próprio site, com 3 meses de teste, aceitando também o CNPJ no novo formato com letras
- Recuperação de senha pelo próprio sistema, por link enviado ao e-mail cadastrado

---

_A MedlyCare mantém ainda o site de apresentação, o manual público e um painel interno de administração da plataforma._
