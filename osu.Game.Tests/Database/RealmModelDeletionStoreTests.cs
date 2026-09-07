// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Database;
using osu.Game.Skinning;

namespace osu.Game.Tests.Database
{
    public class RealmModelDeletionStoreTests : RealmTest
    {
        [TestCase(false)]
        [TestCase(true)]
        public void DeleteAndRestoreAreIdempotent(bool managed)
        {
            RunTestWithRealm((realm, _) =>
            {
                var model = new SkinInfo { ID = Guid.NewGuid(), Name = "Deletion contract" };
                realm.Write(r => { r.Add(model); });
                var input = managed ? model : new SkinInfo { ID = model.ID };
                IModelDeletionStore<SkinInfo> store = new RealmModelDeletionStore<SkinInfo>(realm);

                Assert.That(store.Delete(input), Is.True);
                Assert.That(store.Delete(input), Is.False);
                Assert.That(realm.Run(r => r.Find<SkinInfo>(model.ID)!.DeletePending), Is.True);

                store.Undelete(input);
                store.Undelete(input);
                Assert.That(realm.Run(r => r.Find<SkinInfo>(model.ID)!.DeletePending), Is.False);
            });
        }

        [Test]
        public void MissingModelsAreNotCreated()
        {
            RunTestWithRealm((realm, _) =>
            {
                var model = new SkinInfo { ID = Guid.NewGuid() };
                IModelDeletionStore<SkinInfo> store = new RealmModelDeletionStore<SkinInfo>(realm);
                Assert.That(store.Delete(model), Is.False);
                store.Undelete(model);
                Assert.That(realm.Run(r => r.Find<SkinInfo>(model.ID)), Is.Null);
            });
        }
    }
}
