using AICustomerServiceAPI.Models;
using OpenAiRepository;
using OpenAiRepository.Models;
using OpenAiRepository.Models.Requests;
using System.Text.Json;
using System.Threading;

namespace AICustomerServiceAPI.Services;

public class CustomerAssistantService : ICustomerAssistantService
{
    private readonly OpenAiClient _openAiClient;


    public CustomerAssistantService(OpenAiClient openAiClient)
    {
        this._openAiClient = openAiClient;
    }

    private static Assistant? _assistant;

    public async Task InitializeAssistant()
    {
        string faqFileId = await UpsertProjectFile();

        string vectorStoreId = await UpsertVectorStore(faqFileId);
        
        _assistant = await UpsertAssistant(vectorStoreId);
    }

    public async Task<string> OpenChatConnection()
    {
        var thread = await _openAiClient.Beta.Threads.Create(new CreateThread());
        return thread.Id!;
    }

    public async Task<AssistantResponse> SendUserMessage(string threadId, string messageText)
    {
        // send message from user to assistant
        var message = await _openAiClient.Beta.Threads.Messages.CreateMessage(new CreateMessage
        {
            ThreadId = threadId!,
            Content = messageText,
            Role = "user",
        });

        //create the run on the assistant and thread
        var run = await _openAiClient.Beta.Threads.Runs.Create(new CreateRun
        {
            AssistantId = _assistant!.Id!,
            ThreadId = threadId,
        });

        //wait until the run is completed and the assistant has finished answering
        while (run!.Status != Repository.OpenAi.Models.RunStatus.completed)
        {
            await Task.Delay(500);
            run = await _openAiClient.Beta.Threads.Runs.Retrieve(threadId!, run.Id!);
        }

        //get the messages from the assistant and return the first one 
        var responseMessage = await _openAiClient.Beta.Threads.Messages.List(threadId!, run.Id);
        if (responseMessage.Data.Count == 0)
        {
            throw new InvalidDataException("No response from the assistant");
        }
        string content = responseMessage.Data.First().Content[0].Text!.Value!;
        content = content.Replace("```json", "").Replace("```", ""); //needed to fix the json format
        AssistantResponse assistantResponse = JsonSerializer.Deserialize<AssistantResponse>(content)!;
        return assistantResponse;
    }

    public async Task CloseChatConnection(string threadId)
    {
       await _openAiClient.Beta.Threads.Delete(threadId);
    }

    //helper methods
    private async Task<string> UpsertProjectFile()
    {
        //retrieve all files in the project's store
        string faqFileName = "faq.json";
        string faqFilePath = $"Data/{faqFileName}";
        ListResponse<OpenAiFile> files = await _openAiClient.Files.ListFiles();
        OpenAiFile? faqFile = files.Data.FirstOrDefault(f => f.Filename == faqFileName);
        bool uploadFile = faqFile is null;
        if (faqFile is not null)
        {
            int fileBytes = File.ReadAllBytes(faqFilePath).Length;
            if (faqFile.Bytes != fileBytes)
            {
                //delete the file on the store
                await _openAiClient.Files.DeleteFile(faqFile.Id!);

                uploadFile = true;
            }
        }
        if (uploadFile)
        {
            //create the file
            // create the file
            FileStream? fileStream = File.OpenRead(faqFilePath);
            if (fileStream is not null)
            {
                faqFile = await _openAiClient.Files.CreateFile(new()
                {
                    File = fileStream,
                    FileName = faqFileName,
                });
                fileStream.Close();
            }
        }
        return faqFile!.Id!;
    }

    private async Task<string> UpsertVectorStore(string faqFileId)
    {
        string vectorStoreId = string.Empty;
        string vectorStoreName = "CustomerAssistantVectorStore";
        ListResponse<VectorStore> vectorStores = await _openAiClient.VectorStores.ListVectorStores();
        bool hasFAQVectorStore = vectorStores.Data.Exists(m => m.Name == vectorStoreName);
        if (!hasFAQVectorStore)
        {
            //create vector store
            var vectorStore = await _openAiClient.VectorStores.CreateVectorStore(new CreateVectorStore
            {
                Name = vectorStoreName,
                FileIds = [faqFileId]
            });
            vectorStoreId = vectorStore.Id!;
        }
        else
        {
            //check if the file is in the vector store
            vectorStoreId = vectorStores.Data.First(m => m.Name == vectorStoreName).Id!;
            var vectorStoreFiles = await _openAiClient.VectorStores.LirstVectorStoreFiles(vectorStoreId);
            if (!vectorStoreFiles.Data.Exists(vectorStoreFiles => vectorStoreFiles.Id == faqFileId))
            {
                //add file to vector store
                await _openAiClient.VectorStores.CreateVectorStoreFile(vectorStoreId, new CreateVectorStoreFile
                {
                    FileId = faqFileId,
                });
            }
        }
        return vectorStoreId;
    }

    private async Task<Assistant> UpsertAssistant(string vectorStoreId)
    {
        //Read assistant system message from file
        string filePath = "Data/assistant_system_message.txt";
        string assistantSystemMessage = File.ReadAllText(filePath);

        string assistantTitle = "AI Customer Service API";
        string assistantModel = "gpt-4o-mini";

        ListResponse<Assistant> allAssistants = await _openAiClient.Beta.Assistants.GetAssistants();
        bool hasDefaultAssistant = allAssistants.Data.Exists(m => m.Name == assistantTitle);
        Assistant assistant;
        if (hasDefaultAssistant)
        {
            //check if the instructions match
            assistant = allAssistants.Data.First(m => m.Name == assistantTitle);
            ResponseFormat? responseFormat = null;
            if (!string.IsNullOrEmpty(assistant.ResponseFormat) && assistant.ResponseFormat != "auto")
            {
                responseFormat = JsonSerializer.Deserialize<ResponseFormat>(assistant.ResponseFormat);
            }
            if (AssistantPropertiesNeedUpdate(assistant, assistantSystemMessage, assistantModel, vectorStoreId))
            {
                //update assistant with correct instructions
                assistant = await _openAiClient.Beta.Assistants.ModifyAssistant(new UpdateAssistant
                {
                    Instructions = assistantSystemMessage,
                    Name = assistantTitle,
                    Model = assistantModel,
                    Tools = [new() { Type = AssistantToolType.FileSearch }],
                    ToolResources = new()
                    {
                        FileSearch = new()
                        {
                            VectorStoreIds = [vectorStoreId]
                        }
                    },
                    ResponseFormat = "auto",
                }, assistant.Id!);
            }
        }
        else
        {
            //create default assistant
            assistant = await _openAiClient.Beta.Assistants.CreateAssistant(new()
            {
                Instructions = assistantSystemMessage,
                Name = assistantTitle,
                Model = assistantModel,
                Tools = [new() { Type = AssistantToolType.FileSearch }],
                ToolResources = new()
                {
                    FileSearch = new()
                    {
                        VectorStoreIds = [vectorStoreId]
                    }
                },
                ResponseFormat = "auto",
            });
        }

        return assistant;
    }

    private bool AssistantPropertiesNeedUpdate(Assistant assistant, string assistantSystemMessage, string assistantModel, string vectorStoreId)
    {
        if (assistant.Instructions != assistantSystemMessage // doesn't have same instructions
                || assistant.Model != assistantModel // doesn't have same model
                || assistant.Tools.Length == 0 || assistant.Tools.First().Type != AssistantToolType.FileSearch // doesn't have file search tool
                || assistant.ToolResources is null || assistant.ToolResources.FileSearch is null || assistant.ToolResources.FileSearch.VectorStoreIds.Length == 0 || assistant.ToolResources.FileSearch.VectorStoreIds.First() != vectorStoreId // doesn't have the correct file search tool resource
                || assistant.ResponseFormat is null || assistant.ResponseFormat != "auto" // doesn't have the correct response format
                )
        {
            return false;
        }

        return true;
    }
}
