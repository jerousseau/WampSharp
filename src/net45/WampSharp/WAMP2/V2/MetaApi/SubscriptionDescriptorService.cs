using System;
using System.Linq;
using System.Reactive.Linq;
using WampSharp.V2.Core.Contracts;
using WampSharp.V2.PubSub;
using WampSharp.V2.Realm;

namespace WampSharp.V2.MetaApi
{
    public class SubscriptionDescriptorService : 
        DescriptorServiceBase<SubscriptionDetailsExtended>,
        IWampSubscriptionDescriptor
    {
        public SubscriptionDescriptorService(IWampHostedRealm realm) : base(new SubscriptionMetadataSubscriber(realm.TopicContainer))
        {
            IWampTopicContainer topicContainer = realm.TopicContainer;

            IObservable<IWampTopic> removed = GetTopicRemoved(topicContainer);

            var observable =
                from topic in GetTopicCreated(topicContainer)
                let topicRemoved = removed.Where(x => x == topic)
                let subscriptionAdded = GetSubscriptionAdded(topic, topicRemoved)
                let subscriptionRemoved = GetSubscriptionRemoved(topic, topicRemoved)
                select new { topic, subscriptionAdded, subscriptionRemoved };

            var addObservable =
                from item in observable
                from eventArgs in item.subscriptionAdded
                select new { Topic = item.topic, EventArgs = eventArgs };

            var removeObservable =
                from item in observable
                from eventArgs in item.subscriptionRemoved
                select new { Topic = item.topic, EventArgs = eventArgs };

            addObservable.Subscribe(x => OnSubscriptionAdded(x.Topic, x.EventArgs));
            removeObservable.Subscribe(x => OnSubscriptionRemoved(x.Topic, x.EventArgs));
        }

        private static IObservable<WampSubscriptionAddEventArgs> GetSubscriptionAdded(IWampTopic topic, IObservable<IWampTopic> removed)
        {
            return GetSubscriptionAdded(topic)
                .TakeUntil(removed);
        }

        private static IObservable<WampSubscriptionRemoveEventArgs> GetSubscriptionRemoved(IWampTopic topic, IObservable<IWampTopic> removed)
        {
            return GetSubscriptionRemoved(topic)
                .TakeUntil(removed);
        }

        private static IObservable<IWampTopic> GetTopicRemoved(IWampTopicContainer topicContainer)
        {
            return Observable.FromEventPattern<WampTopicRemovedEventArgs>
                (x => topicContainer.TopicRemoved += x,
                 x => topicContainer.TopicRemoved -= x)
                             .Select(x => x.EventArgs.Topic);
        }

        private static IObservable<IWampTopic> GetTopicCreated(IWampTopicContainer topicContainer)
        {
            return Observable.FromEventPattern<WampTopicCreatedEventArgs>
                (x => topicContainer.TopicCreated += x,
                 x => topicContainer.TopicCreated -= x)
                             .Select(x => x.EventArgs.Topic);
        }

        private static IObservable<WampSubscriptionAddEventArgs> GetSubscriptionAdded(IWampTopic topic)
        {
            return Observable.FromEventPattern<WampSubscriptionAddEventArgs>
                (x => topic.SubscriptionAdded += x,
                 x => topic.SubscriptionAdded -= x)
                             .Select(x => x.EventArgs);
        }

        private static IObservable<WampSubscriptionRemoveEventArgs> GetSubscriptionRemoved(IWampTopic topic)
        {
            return Observable.FromEventPattern<WampSubscriptionRemoveEventArgs>
                (x => topic.SubscriptionRemoved += x,
                 x => topic.SubscriptionRemoved -= x)
                             .Select(x => x.EventArgs);
        }

        private void OnSubscriptionAdded(IWampTopic topic, WampSubscriptionAddEventArgs e)
        {
            IRemoteWampTopicSubscriber subscriber = e.Subscriber;

            long sessionId = subscriber.SessionId;
            long subscriptionId = subscriber.SubscriptionId;

            AddPeer(sessionId, subscriptionId,
                    () => GetSubscriptionDetails(topic,
                                                 sessionId,
                                                 subscriptionId,
                                                 e.Options));
        }

        private SubscriptionDetailsExtended GetSubscriptionDetails
            (IWampTopic topic,
             long sessionId,
             long subscriptionId,
             SubscribeOptions subscribeOptions)
        {
            SubscriptionDetailsExtended result =
                new SubscriptionDetailsExtended()
                {
                    SubscriptionId = subscriptionId,
                    Created = DateTime.Now,
                    Match = subscribeOptions.Match,
                    Uri = topic.TopicUri
                };

            AddGroup(topic.TopicUri, sessionId, result);

            return result;
        }

        private void OnSubscriptionRemoved(IWampTopic topic, WampSubscriptionRemoveEventArgs e)
        {
            RemoveGroup(topic.TopicUri, e.SubscriptionId, e.Session);
        }

        public SubscriptionDetails GetSubscriptionDetails(long subscriptionId)
        {
            SubscriptionDetailsExtended subscriptionDetails = GetGroupDetails(subscriptionId);

            if (subscriptionDetails != null)
            {
                return subscriptionDetails;
            }

            throw new WampException(WampErrors.NoSuchSubscription);
        }

        public long[] GetSubscribers(long subscriptionId)
        {
            SubscriptionDetailsExtended details = GetGroupDetails(subscriptionId);

            if (details != null)
            {
                return details.Subscribers.ToArray();
            }

            throw new WampException(WampErrors.NoSuchSubscription);
        }

        public AvailableGroups GetAllSubscriptionIds()
        {
            return GetAllGroupIds();
        }

        public long LookupSubscriptionId(string topicUri, SubscribeOptions options = null)
        {
            string match = null;

            if (options != null)
            {
                match = options.Match;
            }

            long? subscriptionId = LookupGroupId(topicUri, match);

            if (subscriptionId != null)
            {
                return subscriptionId.Value;
            }

            throw new WampException(WampErrors.NoSuchSubscription);
        }

        public long[] GetMatchingSubscriptionIds(string topicUri)
        {
            long[] matchingSubscriptions = GetMatchingGroupIds(topicUri);

            if (matchingSubscriptions != null)
            {
                return matchingSubscriptions;
            }

            throw new WampException(WampErrors.NoSuchSubscription);
        }

        public long CountSubscribers(long subscriptionId)
        {
            SubscriptionDetailsExtended details = GetGroupDetails(subscriptionId);

            if (details != null)
            {
                return details.Subscribers.Count;
            }

            throw new WampException(WampErrors.NoSuchSubscription);
        }

        private class SubscriptionMetadataSubscriber : ManualSubscriber<IWampSubscriptionMetadataSubscriber>, IWampSubscriptionMetadataSubscriber, IDescriptorSubscriber<SubscriptionDetailsExtended>
        {
            private readonly IWampTopicContainer mTopicContainer;
            private readonly PublishOptions mPublishOptions = new PublishOptions();
            private static readonly string mOnCreateTopicUri = GetTopicUri(subscriber => subscriber.OnCreate(default(long), default(SubscriptionDetails)));
            private static readonly string mOnSubscribeTopicUri = GetTopicUri(subscriber => subscriber.OnSubscribe(default(long), default(long)));
            private static readonly string mOnUnsubscribeTopicUri = GetTopicUri(subscriber => subscriber.OnUnsubscribe(default(long), default(long)));
            private static readonly string mOnDeleteTopicUri = GetTopicUri(subscriber => subscriber.OnDelete(default(long), default(long)));

            public SubscriptionMetadataSubscriber(IWampTopicContainer topicContainer)
            {
                mTopicContainer = topicContainer;
                mTopicContainer.CreateTopicByUri(mOnCreateTopicUri, true);
                mTopicContainer.CreateTopicByUri(mOnSubscribeTopicUri, true);
                mTopicContainer.CreateTopicByUri(mOnUnsubscribeTopicUri, true);
                mTopicContainer.CreateTopicByUri(mOnDeleteTopicUri, true);
            }

            public void OnCreate(long sessionId, SubscriptionDetails details)
            {
                mTopicContainer.Publish(mPublishOptions, mOnCreateTopicUri, sessionId, details);
            }

            public void OnSubscribe(long sessionId, long subscriptionId)
            {
                mTopicContainer.Publish(mPublishOptions, mOnSubscribeTopicUri, sessionId, subscriptionId);
            }

            public void OnUnsubscribe(long sessionId, long subscriptionId)
            {
                mTopicContainer.Publish(mPublishOptions, mOnUnsubscribeTopicUri, sessionId, subscriptionId);
            }

            public void OnDelete(long sessionId, long subscriptionId)
            {
                mTopicContainer.Publish(mPublishOptions, mOnDeleteTopicUri, sessionId, subscriptionId);
            }

            void IDescriptorSubscriber<SubscriptionDetailsExtended>.OnCreate(long sessionId, SubscriptionDetailsExtended details)
            {
                OnCreate(sessionId, details);
            }

            void IDescriptorSubscriber<SubscriptionDetailsExtended>.OnJoin(long sessionId, long groupId)
            {
                OnSubscribe(sessionId, groupId);
            }

            void IDescriptorSubscriber<SubscriptionDetailsExtended>.OnLeave(long sessionId, long groupId)
            {
                OnUnsubscribe(sessionId, groupId);
            }

            void IDescriptorSubscriber<SubscriptionDetailsExtended>.OnDelete(long sessionId, long groupId)
            {
                OnDelete(sessionId, groupId);
            }
        }
    }
}