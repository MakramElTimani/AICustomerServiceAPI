using AICustomerServiceAPI.Models;

namespace AICustomerServiceAPI.Services;

public interface ICustomerAssistantService
{
    /// <summary>
    /// Called on project startup to initialize the assistant with the systen message
    /// Will query Open AI to the assistant and compare if it is the same message, otherwise will create it
    /// Will also add the files to the assistant if they are not already there
    /// </summary>
    Task InitializeAssistant();

    /// <summary>
    /// This method will return the thread id for the assistant
    /// </summary>
    Task<string> OpenChatConnection();

    /// <summary>
    /// Requires the thread id from the OpenChatConnection method
    /// Should return the response from the assistant based on the customer's question
    /// </summary>
    Task<AssistantResponse> SendUserMessage(string threadId, string messageText);

    /// <summary>
    /// Deletes the thread id from the assistant
    /// </summary>
    Task CloseChatConnection(string threadId);

}

