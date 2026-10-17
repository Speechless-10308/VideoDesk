using VideoDesk.Core;

namespace VideoDesk.Core.Tests;

public sealed class CommentSessionTests
{
    [Fact]
    public void Merge_LaggingServerPage_KeepsAcceptedComment()
    {
        var session = new CommentSession();
        var reply = new CommentItem("我", "刚发布的评论", 0, 9001);
        session.Add(42, 0, reply);

        var result = session.Merge(42, []);

        Assert.Equal(reply, Assert.Single(result));
        Assert.Empty(session.Merge(43, []));
    }

    [Fact]
    public void Merge_ServerCatchesUp_DoesNotDuplicateAcceptedComment()
    {
        var session = new CommentSession();
        var reply = new CommentItem("我", "新评论", 0, 9001);
        session.Add(42, 0, reply);

        var result = session.Merge(42, [reply]);

        Assert.Equal(reply.Id, Assert.Single(result).Id);
    }

    [Fact]
    public void Merge_NewNestedReply_AttachesToParentAndSurvivesStalePagination()
    {
        var session = new CommentSession();
        var parent = new CommentItem("UP", "原评论", 0, 9001);
        var child = new CommentItem("我", "新回复", 0, 9002);
        session.Add(42, parent.Id, child);

        var result = session.Merge(42, [parent]);

        Assert.Equal(child, Assert.Single(Assert.Single(result).Replies!));
        Assert.Equal(child, Assert.Single(session.MergeReplies(42, parent.Id, [])));
    }

    [Fact]
    public void Remove_LaggingServerPage_DoesNotResurrectDeletedComment()
    {
        var session = new CommentSession();
        var reply = new CommentItem("我", "已删除", 0, 9001);
        session.Add(42, 0, reply);
        session.Remove(42, reply.Id);

        Assert.Empty(session.Merge(42, [reply]));
    }
}
