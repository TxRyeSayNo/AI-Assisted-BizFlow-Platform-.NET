using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BizFlow.Application.AI;
using BizFlow.Application.Collaboration;
using BizFlow.Application.Common;
using BizFlow.Application.Requests;
using BizFlow.Application.Tasks;
using BizFlow.Domain.Audit;
using BizFlow.Domain.Collaboration;
using BizFlow.Domain.Organization;
using BizFlow.Domain.Requests;
using BizFlow.Domain.Tasks;
using BizFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace BizFlow.Infrastructure.AI;

public sealed class AiActionDispatcher(
    BizFlowDbContext dbContext,
    TaskAssignmentCommand? taskAssignmentCommand = null,
    RequestRoutingCommand? requestRoutingCommand = null,
    CommentService? commentService = null) : IAiActionDispatcher
{
    public async Task<object?> DispatchToolAsync(
        Guid tenantId,
        Guid userId,
        string toolName,
        string argsJson,
        CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(argsJson) ? "{}" : argsJson);
        var root = doc.RootElement;

        switch (toolName.ToLowerInvariant())
        {
            case "create_task_draft":
            {
                var title = root.TryGetProperty("title", out var tp) ? tp.GetString() ?? "" : "Nhiệm vụ mới từ AI";
                var desc = root.TryGetProperty("description", out var dp) ? dp.GetString() : null;
                var priorityStr = root.TryGetProperty("priority", out var pp) ? pp.GetString() : "Medium";
                var priority = Enum.TryParse<TaskPriority>(priorityStr, true, out var p) ? p : TaskPriority.Medium;

                Guid? deptId = root.TryGetProperty("departmentId", out var dip) && dip.TryGetGuid(out var d) ? d : null;
                if (deptId.HasValue)
                {
                    var deptExists = await dbContext.Departments.AnyAsync(dept => dept.TenantId == tenantId && dept.Id == deptId.Value && dept.Status == RecordStatus.Active, cancellationToken);
                    if (!deptExists)
                        throw new ApplicationFault(FaultKind.Validation, "AI.DEPARTMENT_NOT_FOUND", "Department not found in workspace.");
                }

                Guid? assigneeId = root.TryGetProperty("assigneeId", out var aip) && aip.TryGetGuid(out var a) ? a : null;
                if (assigneeId.HasValue)
                {
                    var userExists = await dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == assigneeId.Value && u.DeletedAt == null && u.Status == UserStatus.Active, cancellationToken);
                    if (!userExists)
                        throw new ApplicationFault(FaultKind.Validation, "AI.USER_NOT_FOUND", "Assignee user not found or inactive in workspace.");
                }

                var task = WorkTask.CreateDraft(tenantId, userId, title, DateTimeOffset.UtcNow, desc, priority, null, null);
                dbContext.WorkTasks.Add(task);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new { taskId = task.Id, title = task.Title, status = task.Status.ToString() };
            }

            case "assign_task":
            {
                if (!root.TryGetProperty("taskId", out var tip) || !tip.TryGetGuid(out var taskId))
                    throw new ApplicationFault(FaultKind.Validation, "AI.ARG_REQUIRED", "Argument 'taskId' is required.");

                if (!root.TryGetProperty("userId", out var uip) || !uip.TryGetGuid(out var targetUserId))
                    throw new ApplicationFault(FaultKind.Validation, "AI.ARG_REQUIRED", "Argument 'userId' is required.");

                if (taskAssignmentCommand is not null)
                {
                    var assigned = await taskAssignmentCommand.AssignAsync(
                        taskId,
                        new AssignTaskCommand("USER", targetUserId, "Assigned via AI Agent"),
                        null,
                        cancellationToken);
                    return new { taskId = assigned.TaskId, assignmentId = assigned.AssignmentId, assignedTo = targetUserId, status = assigned.Status };
                }

                var task = await dbContext.WorkTasks
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == taskId && t.DeletedAt == null, cancellationToken)
                    ?? throw new ApplicationFault(FaultKind.NotFound, "TASK.NOT_FOUND", "Task not found in tenant workspace.");

                if (task.Status is not (TaskState.Draft or TaskState.Rejected))
                    throw new ApplicationFault(FaultKind.Conflict, "TASK.TRANSITION_DENIED", $"Cannot assign task in state {task.Status}.");

                var targetUser = await dbContext.Users
                    .FirstOrDefaultAsync(u => u.TenantId == tenantId && u.Id == targetUserId && u.DeletedAt == null && u.Status == UserStatus.Active, cancellationToken)
                    ?? throw new ApplicationFault(FaultKind.Validation, "USER.NOT_FOUND", "Target assignee user not found or inactive in tenant workspace.");

                var assignment = TaskAssignment.Create(task.Id, userId, targetUser.DepartmentId, targetUserId, DateTimeOffset.UtcNow);
                dbContext.TaskAssignments.Add(assignment);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new { taskId = task.Id, assignmentId = assignment.Id, assignedTo = targetUserId };
            }

            case "add_comment":
            {
                if (!root.TryGetProperty("objectId", out var oip) || !oip.TryGetGuid(out var objectId))
                    throw new ApplicationFault(FaultKind.Validation, "AI.ARG_REQUIRED", "Argument 'objectId' is required.");

                var content = root.TryGetProperty("content", out var cp) ? cp.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(content))
                    throw new ApplicationFault(FaultKind.Validation, "AI.ARG_REQUIRED", "Argument 'content' is required.");

                var objTypeStr = root.TryGetProperty("objectType", out var otp) ? otp.GetString() : "Task";
                var objType = Enum.TryParse<CommentObjectType>(objTypeStr, true, out var ot) ? ot : CommentObjectType.Task;

                if (commentService is not null)
                {
                    var added = await commentService.CreateAsync(
                        new CreateCommentCommand(objType == CommentObjectType.Task ? "TASK" : "REQUEST", objectId, content),
                        null,
                        cancellationToken);
                    return new { commentId = added.CommentId, objectId = added.ObjectId, content = added.Content };
                }

                if (content.Trim().Length > 4000 || content.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                    throw new ApplicationFault(FaultKind.Validation, "COMMENT.INVALID_CONTENT", "Comment must be plain text up to 4000 characters.");

                if (objType == CommentObjectType.Task)
                {
                    var taskExists = await dbContext.WorkTasks.AnyAsync(t => t.TenantId == tenantId && t.Id == objectId && t.DeletedAt == null, cancellationToken);
                    if (!taskExists)
                        throw new ApplicationFault(FaultKind.NotFound, "TASK.NOT_FOUND", "Target task not found in tenant workspace.");
                }
                else if (objType == CommentObjectType.Request)
                {
                    var reqExists = await dbContext.Requests.AnyAsync(r => r.TenantId == tenantId && r.Id == objectId && r.DeletedAt == null, cancellationToken);
                    if (!reqExists)
                        throw new ApplicationFault(FaultKind.NotFound, "REQUEST.NOT_FOUND", "Target request not found in tenant workspace.");
                }

                var comment = Comment.Create(tenantId, objType, objectId, userId, content, DateTimeOffset.UtcNow);
                dbContext.Comments.Add(comment);
                await dbContext.SaveChangesAsync(cancellationToken);
                return new { commentId = comment.Id, objectId = comment.ObjectId, content = comment.Content };
            }

            case "route_request":
            {
                if (!root.TryGetProperty("requestId", out var rip) || !rip.TryGetGuid(out var reqId))
                    throw new ApplicationFault(FaultKind.Validation, "AI.ARG_REQUIRED", "Argument 'requestId' is required.");

                Guid? toDeptId = root.TryGetProperty("toDepartmentId", out var dip) && dip.TryGetGuid(out var d) ? d : null;
                Guid? toUserId = root.TryGetProperty("toUserId", out var uip) && uip.TryGetGuid(out var u) ? u : null;
                var reason = root.TryGetProperty("reason", out var rp) ? rp.GetString() : "Điều phối tự động qua AI Agent";

                if (requestRoutingCommand is not null)
                {
                    var routed = await requestRoutingCommand.RouteAsync(
                        reqId,
                        new RouteRequestInput(toDeptId, toUserId, reason, RequestRoutingSource.Ai),
                        null,
                        cancellationToken);
                    return new { requestId = routed.RequestId, routingId = routed.RoutingId, status = routed.Status };
                }

                if (toDeptId is null && toUserId is null)
                    throw new ApplicationFault(FaultKind.Validation, "REQUEST.TARGET_REQUIRED", "At least one target department or user is required.");

                var request = await dbContext.Requests
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Id == reqId && r.DeletedAt == null, cancellationToken)
                    ?? throw new ApplicationFault(FaultKind.NotFound, "REQUEST.NOT_FOUND", "Request not found in tenant workspace.");

                if (request.Status != RequestState.Submitted)
                    throw new ApplicationFault(FaultKind.Conflict, "REQUEST.TRANSITION_DENIED", $"Cannot route request in state {request.Status}.");

                if (toDeptId.HasValue)
                {
                    var deptExists = await dbContext.Departments.AnyAsync(dept => dept.TenantId == tenantId && dept.Id == toDeptId.Value && dept.Status == RecordStatus.Active, cancellationToken);
                    if (!deptExists)
                        throw new ApplicationFault(FaultKind.Validation, "DEPARTMENT.NOT_FOUND", "Target department not found in workspace.");
                }

                if (toUserId.HasValue)
                {
                    var userExists = await dbContext.Users.AnyAsync(u => u.TenantId == tenantId && u.Id == toUserId.Value && u.DeletedAt == null && u.Status == UserStatus.Active, cancellationToken);
                    if (!userExists)
                        throw new ApplicationFault(FaultKind.Validation, "USER.NOT_FOUND", "Target user not found or inactive in workspace.");
                }

                var now = DateTimeOffset.UtcNow;
                var routing = RequestRouting.Create(
                    tenantId, request.Id, userId, now,
                    toDeptId, toUserId, null, null, reason, RequestRoutingSource.Ai);

                var mutation = new RequestWorkflowMutation(userId, request.Status, RequestState.Routed, request.UpdatedAt);
                request.ApplyRoutingTransition(mutation, now);

                dbContext.RequestRoutings.Add(routing);
                dbContext.AuditLogs.Add(AuditLog.RequestRouted(request, routing, now));
                await dbContext.SaveChangesAsync(cancellationToken);
                return new { requestId = request.Id, routingId = routing.Id, status = request.Status.ToString() };
            }

            default:
                throw new ApplicationFault(FaultKind.Validation, "AI.UNKNOWN_TOOL", $"Tool '{toolName}' has no dispatcher implementation.");
        }
    }
}
