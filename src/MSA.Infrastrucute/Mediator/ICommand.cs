using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Mediator;

public interface ICommand
{
}

public interface ICommand<out TResult>
    : IRequest<TResult>, ICommand
{
}

/// <summary>
/// 处理器自己开、提交事务的命令，<c>TransactionBehavior</c> 不再包环境事务。
/// 只给要在事务外做远程调用的热点命令用（例如下单）：环境事务从第一行占住连接直到提交，gRPC 期间也不放。
/// </summary>
public interface ISelfTransactedCommand : ICommand
{
}
