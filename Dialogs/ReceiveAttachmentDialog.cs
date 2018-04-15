using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Connector;

namespace GermanGraphBot.Dialogs
{

  [Serializable]
  public class ReceiveAttachmentDialog : IDialog<Object>
  {
    public async Task StartAsync(IDialogContext context)
    {
      context.Wait(this.MessageReceivedAsync);
    }

    private async Task<byte[]> Upload(string actionUrl, StreamContent imageStream)
    {
      using (var client = new HttpClient())
      using (var formData = new MultipartFormDataContent())
      {
        imageStream.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
          Name = "file",
          FileName = $"{Guid.NewGuid()}.jpg"
        };
        formData.Add(imageStream, "file");
        var response = await client.PostAsync(actionUrl, formData);
        return await response.Content.ReadAsByteArrayAsync();
      }
    }

    public static byte[] ReadFully(Stream input)
    {
      byte[] buffer = new byte[16 * 1024];
      using (MemoryStream ms = new MemoryStream())
      {
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
          ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
      }
    }

    private static async Task<Attachment> GetUploadedAttachmentAsync(string serviceUrl, string conversationId, byte[] data)
    {
      using (var connector = new ConnectorClient(new Uri(serviceUrl)))
      {
        var attachments = new Attachments(connector);
        var response = await attachments.Client.Conversations.UploadAttachmentAsync(
          conversationId,
          new AttachmentData
          {
            Name = "returnedImage.jpg",
            OriginalBase64 = data,
            Type = "image/jpg"
          });

        var attachmentUri = attachments.GetAttachmentUri(response.Id);

        return new Attachment
        {
          Name = "returnedImage.jpg",
          ContentType = "image/jpg",
          ContentUrl = attachmentUri
        };
      }
    }


    public virtual async Task MessageReceivedAsync(IDialogContext context, IAwaitable<IMessageActivity> argument)
    {
      var message = await argument;
      var replyMessage = context.MakeMessage();

      if (message.Attachments != null && message.Attachments.Any())
      {
        foreach (var attachment in message.Attachments)
        {
          if (attachment.ContentType == "image/jpeg" || attachment.ContentType == "image/jpg")
          {
            await context.PostAsync(new Random().Next(0, 2) == 1
              ? "Your image is accepted! Please wait patiently, we have the windows 95 server :("
              : "Your image is accepted! Our hard working Indians are ready to go!");
            using (HttpClient httpClient = new HttpClient())
            {
              // Skype & MS Teams attachment URLs are secured by a JwtToken, so we need to pass the token from our bot.
              if ((message.ChannelId.Equals("skype", StringComparison.InvariantCultureIgnoreCase) || message.ChannelId.Equals("msteams", StringComparison.InvariantCultureIgnoreCase))
                  && new Uri(attachment.ContentUrl).Host.EndsWith("skype.com"))
              {
                var token = await new MicrosoftAppCredentials().GetTokenAsync();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
              }

              try
              {
                var imageData = await httpClient.GetAsync(attachment.ContentUrl);
                var responseImage = await Upload("https://germangraph.azurewebsites.net/api/upload", (StreamContent)imageData.Content);
                var reply = await GetUploadedAttachmentAsync(replyMessage.ServiceUrl, replyMessage.Conversation.Id, responseImage);
                replyMessage.Attachments = new List<Attachment> { reply };
                await context.PostAsync(replyMessage);
              }
              catch (Exception)
              {
                await context.PostAsync($"Something went wrong :( Please try again.");
              }
            }
          }
        }
      }
      else
      {
        await context.PostAsync("Hello! I'm a bot that can detect different logos on your photos. Please send me one!");
      }

      context.Wait(this.MessageReceivedAsync);
    }
  }
}