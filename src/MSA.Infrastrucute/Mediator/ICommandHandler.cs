using MediatR;
using System;
using System.Collections.Generic;
using System.Text;

namespace MSA.Infrastrucute.Mediator;

public interface ICommandHandler<in TCommand, TResult>
    : IRequestHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
}
