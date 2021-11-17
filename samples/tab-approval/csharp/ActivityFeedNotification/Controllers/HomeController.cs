﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Graph;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using TabActivityFeed.Helpers;
using TabActivityFeed.Model;
using TabActivityFeed.Repository;

namespace TabActivityFeed.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ConcurrentDictionary<string, List<TaskInfo>> _taskList;

        public HomeController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor,
            ConcurrentDictionary<string, List<TaskInfo>> taskList)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
            _taskList = taskList;
        }

        [Route("request")]
        public ActionResult Hello()
        {
            var currentTaskList = new List<TaskInfo>();
            _taskList.TryGetValue("taskList", out currentTaskList);
            if (currentTaskList == null)
            {
                ViewBag.Message = "No request found";
            }
            else
            {
                ViewBag.TaskList = currentTaskList;
            }
            return View("Index");
        }

        [HttpPost]
        [Route("SendNotificationToManager")]
        public async Task<ActionResult> SendNotificationToManager(TaskInfo taskInfo)
        {
            // TaskHelper.AddTaskToFeed(taskInfo);
            var currentTaskList = new List<TaskInfo>();
            List<TaskInfo> taskList = new List<TaskInfo>();
            _taskList.TryGetValue("taskList", out currentTaskList);
            var request = taskInfo;
            request.taskId = Guid.NewGuid();
            request.status = "Pending";
            if (currentTaskList == null)
            {
                taskList.Add(request);
                _taskList.AddOrUpdate("taskList", taskList, (key, newValue) => taskList);
                ViewBag.TaskList = taskList;
            }
            else
            {
                currentTaskList.Add(request);
                _taskList.AddOrUpdate("taskList", currentTaskList, (key, newValue) => currentTaskList);
                ViewBag.TaskList = currentTaskList;
            }

            var graphClient = SimpleGraphClient.GetGraphClient(taskInfo.access_token);
            var graphClientApp = SimpleGraphClient.GetGraphClientforApp(_configuration["AzureAd:MicrosoftAppId"], _configuration["AzureAd:MicrosoftAppPassword"], _configuration["AzureAd:TenantId"]);
            var user = await graphClient.Users[taskInfo.managerName]
                      .Request()
                      .GetAsync();
            var installedApps = await graphClient.Users[user.Id].Teamwork.InstalledApps
                               .Request()
                               .Expand("teamsAppDefinition")
                               .GetAsync();
            var installationId = installedApps.Where(id => id.TeamsAppDefinition.DisplayName == "Tab Approval").Select(x => x.Id);
            var userName = user.UserPrincipalName;

                ViewBag.taskID = new Guid();
                var topic = new TeamworkActivityTopic
                {
                    Source = TeamworkActivityTopicSource.EntityUrl,
                    Value = "https://graph.microsoft.com/beta/users/" + user.Id + "/teamwork/installedApps/" + installationId.ToList()[0]
                };

            var activityType = "approvalRequired";

            var previewText = new ItemBody
            {
                Content = "Deployment requires your approval"
            };
            var customRecipient = new AadUserNotificationRecipient
            {
                UserId = user.Id
            };
            var templateParameters = new List<Microsoft.Graph.KeyValuePair>()
                 {
                 new Microsoft.Graph.KeyValuePair
                 {
                   Name = "approvalTaskId",
                   Value ="2020AAGGTAPP"
                  }
                };
            try
                {
                    await graphClientApp.Users[user.Id].Teamwork
                        .SendActivityNotification(topic, activityType, null, previewText, templateParameters)
                        .Request()
                        .PostAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

            return View("Index");
        }

        [HttpPost]
        [Route("SendNotificationToUser")]
        public async Task<ActionResult> SendNotificationToUser(TaskInfo taskInfo)
        {
            var currentTaskList = new List<TaskInfo>();
            _taskList.TryGetValue("taskList", out currentTaskList);

            var requestUpdate = currentTaskList.FirstOrDefault(p => p.taskId == taskInfo.taskId);
            requestUpdate.status = taskInfo.status;
            _taskList.AddOrUpdate("taskList", currentTaskList, (key, newValue) => currentTaskList);
             ViewBag.TaskList = currentTaskList;
            
            return View("Index");
        }

        [Authorize]
        [HttpGet("/GetUserAccessToken")]
        public async Task<ActionResult<string>> GetUserAccessToken()
        {
            try
            {
                var accessToken = await AuthHelper.GetAccessTokenOnBehalfUserAsync(_configuration, _httpClientFactory, _httpContextAccessor);
                return accessToken;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}