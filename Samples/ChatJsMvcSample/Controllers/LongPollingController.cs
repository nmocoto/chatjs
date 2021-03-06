﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Web;
using System.Web.Mvc;
using ChatJsMvcSample.Code;
using ChatJsMvcSample.Code.LongPolling;
using ChatJsMvcSample.Code.LongPolling.Chat;

namespace ChatJsMvcSample.Controllers
{
    public class LongPollingController : Controller
    {
        private readonly List<LongPollingProvider> providers = new List<LongPollingProvider>();

        /// <summary>
        /// This STUB. In a normal situation, there would be multiple rooms and the user room would have to be 
        /// determined by the user profile
        /// </summary>
        public const string ROOM_ID_STUB = "chatjs-room";

        /// <summary>
        /// Returns my user id
        /// </summary>
        /// <returns></returns>
        private int GetMyUserId(HttpRequestBase request)
        {
            // This would normally be done like this:
            //var userPrincipal = this.Context.User as AuthenticatedPrincipal;
            //if (userPrincipal == null)
            //    throw new NotAuthorizedException();

            //var userData = userPrincipal.Profile;
            //return userData.Id;

            // But for this example, it will get my user from the cookie
            return ChatHelper.GetChatUserFromCookie(request).Id;
        }

        private string GetMyRoomId()
        {
            // This would normally be done like this:
            //var userPrincipal = this.Context.User as AuthenticatedPrincipal;
            //if (userPrincipal == null)
            //    throw new NotAuthorizedException();

            //var userData = userPrincipal.Profile;
            //return userData.MyTenancyIdentifier;

            // But for this example, it will always return "chatjs-room", because we have only one room.
            return ROOM_ID_STUB;
        }

        public LongPollingController()
        {
            // register providers here
            this.providers.Add(new ChatLongPollingProvider());

            foreach (var provider in this.providers)
                provider.Initialize();
        }

        /// <summary>
        /// Returns long polling evetns to the client.
        /// If there are not events right now. This action will STOP and wait for them
        /// </summary>
        /// <param name="timeStamp"></param>
        /// <returns></returns>
        [HttpGet]
        public JsonResult GetEvents(long timeStamp)
        {
            var roomId = this.GetMyRoomId();
            var myUserId = this.GetMyUserId(this.Request);

            var eventsReturned = new List<LongPollingEvent>();
            var eventsReturnedLock = new object();

            var wait = new AutoResetEvent(false);

            foreach (var provider in this.providers)
            {
                // for each provider, let's create a different thread that will listen to that particular kind
                // of event. When the first returns, this Action returns as well
                ThreadPool.QueueUserWorkItem(providerClosure =>
                    {
                        try
                        {
                            var events = ((LongPollingProvider)providerClosure).WaitForEvents(myUserId, roomId, timeStamp, null, this).ToList();
                            // if the provider returned no events, we're not going to continue. Maybe another provider will return
                            // something later on (the new appointment provider returns immediately when no appointments exists, without
                            // this IF, it would always stop the longpolling without waiting for the chat.)
                            if (events.Any())
                                lock (eventsReturnedLock)
                                {
                                    Debug.WriteLine("Finally! Got EVENTS");
                                    eventsReturned.AddRange(events);
                                    wait.Set();
                                }
                        }
                        catch
                        {
                            // The long polling cannot stop because a provider triggered an exception
                            // ADD SOME AZURE DIAGNOSTICS HERE
                        }

                    }, provider);
            }

            wait.WaitOne(LongPollingProvider.WAIT_TIMEOUT);

            return this.Json(new
            {
                Events = eventsReturned,
                Timestamp = DateTime.UtcNow.Ticks.ToString()
            }, JsonRequestBehavior.AllowGet);
        }
    }
}